// SPDX-License-Identifier: MIT
// Smashing props, felling trees, smashing bee hives around each player once per second.
// LootServer starts a pass (Run) once per second per player on the tick thread, and calls Resume in the next server steps
// while that pass is not finished (Pending).
//
// C# picks the targets (the template's kind from Data.Smash, read from the game, the switches, the player's two states) and runs a
// one-target program per hit (SmashHit). Because the loop runs in C#:
//   - at most SmashRules.MaxTargetsPerStep (4) targets per player per pass and in any second (a pass that found its
//     targets late in its second does not let the next one add four more at once), nearest first;
//   - a pass looks at the targets nearest first and ends at the 4 nearest wanted ones or at the end of the list. In one
//     server step all players' passes together spend at most RunBudgetMicros and cast at most MaxRaysPerStep rays in at
//     most MaxRayMicrosPerStep (a quarter of the game's shared ray budget, Kiln.Queries: 64 rays and 500 us a step for
//     every mod and every creature's sight), and a pass stops for the step when either is spent or the game refuses a
//     ray (BudgetExceeded); then it goes on from there in the next step, until the next pass starts a second later.
//     LootServer.SmashStep lets the players take turns going first (BeginStep). Before 0.2.12 the rest waited for the
//     next second's pass, which began again at the nearest target: with more props out of sight near the player than
//     one step's rays cover (a cellar or the next room of a ruin, inside 15 m), every pass spent its rays on the same
//     nearest hidden ones, their answers ran out before the pass came back to them, and props in plain view behind them
//     were not looked at for a minute or more (HadToRegister, 2026-11-26; lab: tests/LabSmash Pace_cellar_below). A
//     pass looks at each target at most once, so a second costs at most one look at what is in reach;
//     each target's template is read once (Spawn.Inspect) and kept while it stays in reach;
//   - a target is hit again only after RetryMs (it needs a step or two to break), and after MaxTries hits that did
//     not break it, it is left alone for GiveUpMs (otherwise one target that never breaks would hold a slot forever);
//   - with "Only what I can see" on and line of sight there, only a target the player can see is hit (0.2.8), each
//     answer kept SightCacheMs. A check looks at the prop itself (BodySightHeight), then at the point above it
//     (AboveSightHeight, the only one before 0.2.12: under a low ceiling it lay in the ceiling for a barrel on a stack
//     or a shelf, which was never smashed though in plain view; lab Pace_built_room); elsewhere the height window decides (as for looting, LootLoop.SightDecides). A check that
//     does not answer (a tile not loaded yet) leaves that one target to the height window for SightRetryMs, then it is
//     asked again; before 0.2.12 one such answer turned line of sight off for every target for 5 s, and props behind
//     walls and under floors were smashed meanwhile (lab: Pace_built_room, Pace_cellar_below).
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class Smash
{
    public const long CandidateCacheMs = 250, RetryMs = 1500, GiveUpMs = 30_000;
    public const int MaxTries = 3;
    public const long RunBudgetMicros = 4_000;   // all passes' looks in one server step; a pass goes on in the next
    // Smashing's share of the game's ray budget in one server step, all players together: a quarter of the 64 rays and of
    // the 500 us that every mod's rays and every creature's sight share (Kiln.Queries), so a pass that goes on step after
    // step leaves the rest of every step to them (review of 0.2.12: a pass took every ray the game had left).
    public const int MaxRaysPerStep = 16;
    public const long MaxRayMicrosPerStep = 125;
    public const long KnownForgetMs = 2_500;     // a template kept for a target no Run saw this long is read again
    public const long SightCacheMs = 1_500;      // a line-of-sight answer per player and target is kept this long
    // ...and a Hidden one up to HiddenKeepMs while the player stays within SightStillMetres of where it was asked: a
    // player standing in a ruin room is not asked about the cellar below every 1.5 s, so the pass gets on to the props
    // in view with smashing's share of the rays (review of 0.2.12). Keeping "hidden" longer only ever waits.
    public const long HiddenKeepMs = 5_000;
    public const double SightStillMetres = 1.0;
    public const long SightRetryMs = 5_000;      // a target's check did not answer: the height window judges it this long
    // Where a check looks, from the player's eye: the prop itself (0.6 m above its origin, the middle of a barrel or a
    // crate), then, only when that is blocked, the point 1.5 m above it (all that was looked at before 0.2.12). Both are
    // in the target's own origin column, never moved sideways past a wall (as SightTrace's corpse heights).
    public const double BodySightHeight = 0.6, AboveSightHeight = 1.5;

    /// <summary>What the last Run or Resume did in its server step (the "slow smash" line). <paramref name="Stopped"/>: the
    /// pass goes on in the next step (its time in this step is spent, or the game's ray budget).</summary>
    internal readonly record struct RunWork(int InReach, int Looked, int New, long InspectMicros, long SlowestInspect, bool Stopped);

    /// <summary>One player's pass: the targets in reach when it started (nearest first), how far it got, what it hit.</summary>
    private sealed class Pass(IReadOnlyList<EntityId> inReach, long startedMs)
    {
        public readonly IReadOnlyList<EntityId> InReach = inReach;
        public readonly long StartedMs = startedMs;
        public int Next;
        public int Hits;
        public bool Done => Hits >= SmashRules.MaxTargetsPerStep || Next >= InReach.Count;
    }

    // per player: the pass that is not finished yet
    private readonly Dictionary<uint, Pass> _passes = new();
    // per player: when their latest hits went out, oldest first (at most MaxTargetsPerStep in any second)
    private readonly Dictionary<uint, Queue<long>> _recentHits = new();
    // this server step (BeginStep): smashing's time, rays and ray time, every player's pass together; whose turn it is
    private long _stepMicros, _stepRayMicros;
    private int _stepRays, _turn;

    internal RunWork LastRun { get; private set; }

    private readonly Func<long> _micros;
    // per target: its template and serial from its first Spawn.Inspect, and when a Run last saw it
    private readonly Dictionary<uint, (Guid Template, ulong Serial, long Stamp, long SeenMs)> _known = new();
    // per player and target: what line of sight said, until when, and when and from where it was asked
    private readonly Dictionary<(uint Player, uint Target), (long Until, ulong Serial, long Stamp, Sight Answer, long AskedMs, WorldPosition From)> _sight = new();
    private long _lastKnownPrune;

    private readonly ISmashWorld _world;
    private readonly Func<long> _nowMs;
    private SmashTable? _table;
    private readonly Func<SmashTable> _loadTable;

    // the Destructible list of the last few hundred ms, shared by every player's Run
    private IReadOnlyList<EntityId> _candidates = [];
    private long? _candidatesAt;
    // per target (a world entity, shared by all players): hits sent and when
    private readonly Dictionary<uint, Tries> _tries = new();
    private long _lastPrune;

    private sealed class Tries
    {
        public ulong Serial;
        public long Stamp;
        public int Count;
        public long LastMs;
        public long IgnoreUntilMs;
    }

    public Smash(Data data) : this(data, new KilnSmashWorld(), () => Environment.TickCount64, () => data.Smash) { }

    internal Smash(Data data, ISmashWorld world, Func<long> nowMs, Func<SmashTable> table, Func<long>? micros = null)
    {
        _micros = micros ?? LootLoop.StopwatchMicros;
        Data = data;
        _world = world;
        _nowMs = nowMs;
        _loadTable = table;
    }

    public Data Data { get; }

    internal SmashTable Table => _table ??= _loadTable();

    /// <summary>Told after each hit (the debug log while counterLog is on): the target, its kind and the switches used.</summary>
    public Action<SmashTarget, SmashKind, SmashSwitches>? Hit { get; set; }

    /// <summary>At ServerStartedEvent (per hosted world).</summary>
    public void Start()
    {
        var t = Table;
        SmashHit.Reset();
        KilnLog.Info($"kiln-auto-loot: smash: {t.Count} templates (smash {t.CountOf(SmashKind.Smash)}, shared {t.CountOf(SmashKind.Shared)}, " +
                     $"stones {t.CountOf(SmashKind.Stone) + t.CountOf(SmashKind.StoneShared)}, trees {t.CountOf(SmashKind.Tree)}, " +
                     $"hives {t.CountOf(SmashKind.Beehive) + t.CountOf(SmashKind.BeehiveShared)})");
    }

    /// <summary>At ServerStoppingEvent: drop every per-world state.</summary>
    public void Stop()
    {
        _candidates = [];
        _candidatesAt = null;
        _passes.Clear();
        _recentHits.Clear();
        BeginStep(0);
        _tries.Clear();
        _known.Clear();
        _sight.Clear();
    }

    /// <summary>Once per second per player, tick thread: starts a new pass (an unfinished one of this player is dropped).
    /// Does nothing when auto loot (s.Looting, F9) or s.Smash is off or the limits forbid it. Only targets the player can
    /// see are hit where line of sight decides, else only those inside the height window (heightAbove / heightBelow).</summary>
    public void Run(EntityId player, WorldPosition position, PersonalSettings s, ServerLimits limits)
    {
        _passes.Remove(player.Value);
        LastRun = default;
        var sw = SmashSwitches.Of(s, limits);
        if (sw.Off) return;                                       // smashing off or no radius: nothing found
        if (_world.InSaveZone(player)) return;                    // nothing at all in a save zone

        var now = _nowMs();
        var table = Table;
        if (table.Count == 0) return;
        if (_candidatesAt is not { } at || now - at >= CandidateCacheMs)
        {
            _candidates = _world.Candidates();
            _candidatesAt = now;
        }
        PruneTries(now);
        var pass = new Pass(_world.Within(_candidates, position, sw.Radius), now);   // nearest first
        Slice(player, position, pass, s, sw, table, now);
        if (!pass.Done) _passes[player.Value] = pass;
    }

    /// <summary>At the start of every server step, before any player's Run or Resume (LootServer.SmashStep): the step's
    /// smash time and rays start from nothing. Returns the index of the player (of <paramref name="players"/>) who goes
    /// first in this step: they take turns, so one player's pass that wants every ray never leaves another without.</summary>
    public int BeginStep(int players)
    {
        _stepMicros = _stepRayMicros = 0;
        _stepRays = 0;
        if (players <= 0) return _turn = 0;
        return _turn = (_turn + 1) % players;
    }

    /// <summary>The player has a pass that goes on in the next server step (call <see cref="Resume"/> then).</summary>
    public bool Pending(EntityId player) => _passes.ContainsKey(player.Value);

    /// <summary>The next server step of the player's unfinished pass, tick thread; the switches, the limits and the save
    /// zone are checked again, the radius and the height window from <paramref name="position"/> (where the player is
    /// now), and a pass a second old gives way to the next one.</summary>
    public void Resume(EntityId player, WorldPosition position, PersonalSettings s, ServerLimits limits)
    {
        if (!_passes.TryGetValue(player.Value, out var pass)) return;
        var now = _nowMs();
        var sw = SmashSwitches.Of(s, limits);
        if (sw.Off || now - pass.StartedMs >= 1000 || Table.Count == 0 || _world.InSaveZone(player))
        {
            _passes.Remove(player.Value);
            LastRun = default;
            return;
        }
        Slice(player, position, pass, s, sw, Table, now);
        if (pass.Done) _passes.Remove(player.Value);
    }

    /// <summary>One server step of a pass: from where it stopped, nearest first, until it has its 4, the list ends, the
    /// step's RunBudgetMicros are spent (all players together) or the step's rays are (smashing's share or the game's
    /// budget: then it stops at that target, to ask again).</summary>
    private void Slice(EntityId player, WorldPosition position, Pass pass, PersonalSettings s, SmashSwitches sw, SmashTable table, long now)
    {
        bool? near = null;
        bool NearBuildZone() => near ??= _world.NearBuildZone(player);
        var sightDecides = s.VisibleOnly && _world.SightAvailable;

        var wanted = new List<(double Dist, SmashTarget Target, SmashKind Kind)>();
        var started = _micros();
        var inReach = pass.InReach;
        int looked = 0, fresh = 0;
        long inspectMicros = 0, slowest = 0;
        var stopped = false;
        // the pass's own 4, and no more than 4 in the last second (the previous pass may have hit late in its second)
        var room = Math.Min(SmashRules.MaxTargetsPerStep - pass.Hits, HitsLeft(player, now));
        for (; pass.Next < inReach.Count; pass.Next++)
        {
            var e = inReach[pass.Next];
            if (wanted.Count >= room) break;   // the nearest wanted ones are found, or this second's hits are
            if (looked > 0 && _stepMicros + (_micros() - started) >= RunBudgetMicros)
            {
                stopped = true;   // the rest goes on in the next server step
                break;
            }
            looked++;
            SmashTarget? found;
            if (_known.TryGetValue(e.Value, out var k) && k.Stamp == _world.SpawnStamp(e))
            {
                found = _world.PositionOf(e) is { } here ? new SmashTarget(e, k.Template, k.Serial, here, k.Stamp) : null;
                if (found is null) _known.Remove(e.Value);
                else _known[e.Value] = (k.Template, k.Serial, k.Stamp, now);
            }
            else
            {
                var t0 = _micros();
                found = _world.Inspect(e);
                var took = _micros() - t0;
                fresh++;
                inspectMicros += took;
                if (took > slowest) slowest = took;
                if (found is { } f) _known[e.Value] = (f.Template, f.Serial, f.Stamp, now);
            }
            if (found is not { } target || !table.TryGet(target.Template, out var kind))
            {
                ProbeTrace.Note(e, "smash: not a smash target");
                continue;
            }
            if (!sightDecides && !HeightWindow.Allows(target.Position.Y - position.Y, s))
            {
                ProbeTrace.Note(e, $"smash: {kind}, outside the height window");
                continue;   // the player's height window
            }
            ProbeTrace.Note(e, $"smash: {kind}, wanted {SmashRules.Wants(kind, sw, NearBuildZone)} (stones {sw.Stones}, " +
                               $"buildables {sw.Buildables}, trees {sw.Trees}, hives {sw.Hives}), may hit {MayHit(target, now)}");
            if (!SmashRules.Wants(kind, sw, NearBuildZone) || !MayHit(target, now)) continue;
            if (sightDecides)
            {
                var seen = Look(player, target, position, now);
                if (seen == Sight.Later)
                {
                    stopped = true;   // this step's rays are spent (ours or the game's): ask this one again in the next step
                    break;
                }
                if (seen == Sight.Hidden)
                {
                    ProbeTrace.Note(e, $"smash: {kind}, not in plain view");
                    continue;
                }
                // this one's check did not answer: the height window judges it (this target only)
                if (seen == Sight.Unknown && !HeightWindow.Allows(target.Position.Y - position.Y, s))
                {
                    ProbeTrace.Note(e, "smash: sight did not answer, outside the height window fallback");
                    continue;
                }
            }
            var dist = target.Position.DistanceTo(position);
            if (dist <= sw.Radius) wanted.Add((dist, target, kind));
        }
        LastRun = new RunWork(inReach.Count, looked, fresh, inspectMicros, slowest, stopped);
        wanted.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        foreach (var (_, target, kind) in wanted)
        {
            pass.Hits++;
            if (!_recentHits.TryGetValue(player.Value, out var recent)) _recentHits[player.Value] = recent = new();
            recent.Enqueue(now);
            Remember(target, now);
            _known.Remove(target.Entity.Value);   // a hit target breaks and its id may come back as another: read it anew
            ProbeTrace.Note(target.Entity, $"smash: hit as {kind}");
            _world.Hit(player, target, kind);
            Hit?.Invoke(target, kind, sw);
        }
        _stepMicros += _micros() - started;
    }

    public void PlayerLeft(EntityId player)
    {
        _passes.Remove(player.Value);   // the tries belong to the targets
        _recentHits.Remove(player.Value);
    }

    /// <summary>How many more hits the player may have in this second (SmashRules.MaxTargetsPerStep in any second).</summary>
    private int HitsLeft(EntityId player, long now)
    {
        if (!_recentHits.TryGetValue(player.Value, out var recent)) return SmashRules.MaxTargetsPerStep;
        while (recent.Count > 0 && now - recent.Peek() >= 1000) recent.Dequeue();
        return SmashRules.MaxTargetsPerStep - recent.Count;
    }

    /// <summary>Line of sight from the player to the target, at the prop itself and, when that is blocked or does not
    /// answer, at the point above it. Visible: either is clear, kept <see cref="SightCacheMs"/>. Hidden: one is blocked
    /// and the other is not clear, kept <see cref="SightCacheMs"/>, and up to <see cref="HiddenKeepMs"/> while the
    /// player stays within <see cref="SightStillMetres"/> of where it was asked. Unknown: neither answered, the height
    /// window judges this target, kept <see cref="SightRetryMs"/> (after <see cref="SightCacheMs"/> only while the
    /// player stays put); before the review a body check that did not answer went to the height window without asking
    /// the point above, which 0.2.11 would have found blocked. Later: this server step's rays are spent (smashing's
    /// share, <see cref="MaxRaysPerStep"/> and <see cref="MaxRayMicrosPerStep"/>, or the game's budget), nothing is
    /// kept (both are asked again in the next step).</summary>
    private Sight Look(EntityId player, in SmashTarget t, WorldPosition position, long now)
    {
        var key = (player.Value, t.Entity.Value);
        if (_sight.TryGetValue(key, out var c) && c.Until > now && c.Serial == t.Serial && c.Stamp == t.Stamp &&
            (now - c.AskedMs < SightCacheMs || c.From.DistanceTo(position) <= SightStillMetres)) return c.Answer;
        // a look takes up to two rays: with fewer left of smashing's share in this step it waits for the next
        if (_stepRays + 2 > MaxRaysPerStep || _stepRayMicros >= MaxRayMicrosPerStep) return Sight.Later;
        var answer = Ray(player, t.Entity, BodySightHeight);
        if (answer is Sight.Hidden or Sight.Unknown)
            answer = Ray(player, t.Entity, AboveSightHeight) switch
            {
                Sight.Visible => Sight.Visible,
                Sight.Hidden => Sight.Hidden,   // a known blockage there stands, whatever the body's ray said
                Sight.Later => Sight.Later,
                _ => answer,                    // the second did not answer: the body's Hidden stands, or neither answered
            };
        if (answer == Sight.Later) return answer;   // the pass asks again in the next step
        var keep = answer switch { Sight.Unknown => SightRetryMs, Sight.Hidden => HiddenKeepMs, _ => SightCacheMs };
        _sight[key] = (now + keep, t.Serial, t.Stamp, answer, now, position);
        return answer;
    }

    /// <summary>One line-of-sight ray, counted against smashing's share of the step (a BudgetExceeded refusal casts
    /// nothing; its time still counts).</summary>
    private Sight Ray(EntityId player, EntityId target, double height)
    {
        var t0 = _micros();
        var answer = _world.LineOfSight(player, target, height);
        _stepRayMicros += _micros() - t0;
        if (answer != Sight.Later) _stepRays++;
        return answer;
    }

    private bool MayHit(in SmashTarget t, long now)
    {
        if (!_tries.TryGetValue(t.Entity.Value, out var tr) || tr.Serial != t.Serial || tr.Stamp != t.Stamp) return true;
        if (now < tr.IgnoreUntilMs) return false;
        return now - tr.LastMs >= RetryMs;
    }

    private void Remember(in SmashTarget t, long now)
    {
        if (!_tries.TryGetValue(t.Entity.Value, out var tr) || tr.Serial != t.Serial || tr.Stamp != t.Stamp)
            _tries[t.Entity.Value] = tr = new Tries { Serial = t.Serial, Stamp = t.Stamp };
        if (tr.IgnoreUntilMs != 0 && now >= tr.IgnoreUntilMs) { tr.Count = 0; tr.IgnoreUntilMs = 0; }
        tr.Count++;
        tr.LastMs = now;
        if (tr.Count >= MaxTries) tr.IgnoreUntilMs = now + GiveUpMs;
    }

    private void PruneTries(long now)
    {
        if (now - _lastKnownPrune >= 1000)
        {
            _lastKnownPrune = now;
            List<uint>? old = null;
            foreach (var (id, k) in _known)
                if (now - k.SeenMs > KnownForgetMs) (old ??= []).Add(id);
            if (old is not null) foreach (var id in old) _known.Remove(id);
            List<(uint, uint)>? stale = null;
            foreach (var (key, c) in _sight)
                if (c.Until <= now) (stale ??= []).Add(key);
            if (stale is not null) foreach (var key in stale) _sight.Remove(key);
        }
        if (now - _lastPrune < 10_000) return;
        _lastPrune = now;
        List<uint>? gone = null;
        foreach (var (id, tr) in _tries)
            if (now - tr.LastMs > GiveUpMs * 2 && now >= tr.IgnoreUntilMs) (gone ??= []).Add(id);
        if (gone is not null) foreach (var id in gone) _tries.Remove(id);
    }
}
