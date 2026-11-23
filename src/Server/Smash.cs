// SPDX-License-Identifier: MIT
// Smashing props, felling trees, smashing bee hives around each player once per second.
// LootServer calls Run once per second per player on the tick thread.
//
// C# picks the targets (the template's kind from Data.Smash, read from the game, the switches, the player's two states) and runs a
// one-target program per hit (SmashHit). Because the loop runs in C#:
//   - at most SmashRules.MaxTargetsPerStep (4) targets per player per call, nearest first;
//   - targets are looked at nearest first, and the look stops at the 4 nearest wanted ones or when RunBudgetMicros are
//     spent (a village of props the mod has never looked at, after a fast travel: the rest waits for the next second);
//     each target's template is read once (Spawn.Inspect) and kept while it stays in reach;
//   - a target is hit again only after RetryMs (it needs a step or two to break), and after MaxTries hits that did
//     not break it, it is left alone for GiveUpMs (otherwise one target that never breaks would hold a slot forever);
//   - with "Only what I can see" on and line of sight there, only a target the player can see is hit (0.2.8), each
//     answer kept SightCacheMs; elsewhere the height window decides (as for looting, LootLoop.SightDecides).
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class Smash
{
    public const long CandidateCacheMs = 250, RetryMs = 1500, GiveUpMs = 30_000;
    public const int MaxTries = 3;
    public const long RunBudgetMicros = 4_000;   // one Run's look at the targets; the rest waits for the next second
    public const long KnownForgetMs = 2_500;     // a template kept for a target no Run saw this long is read again
    public const long SightCacheMs = 1_500;      // a line-of-sight answer per player and target is kept this long
    public const long SightRetryMs = 5_000;      // line of sight did not answer: the height window stands in this long

    /// <summary>What the last Run did (the "slow smash" line).</summary>
    internal readonly record struct RunWork(int InReach, int Looked, int New, long InspectMicros, long SlowestInspect, bool Stopped);

    internal RunWork LastRun { get; private set; }

    private readonly Func<long> _micros;
    // per target: its template and serial from its first Spawn.Inspect, and when a Run last saw it
    private readonly Dictionary<uint, (Guid Template, ulong Serial, long Stamp, long SeenMs)> _known = new();
    // per player and target: what line of sight said, until when
    private readonly Dictionary<(uint Player, uint Target), (long Until, ulong Serial, long Stamp, bool Visible)> _sight = new();
    private long _sightUnknownUntil;
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
        _tries.Clear();
        _known.Clear();
        _sight.Clear();
        _sightUnknownUntil = 0;
    }

    /// <summary>Once per second per player, tick thread. Does nothing when auto loot (s.Looting, F9) or s.Smash is off or the limits forbid it. Only
    /// targets the player can see are hit where line of sight decides, else only those inside the height window
    /// (heightAbove / heightBelow).</summary>
    public void Run(EntityId player, WorldPosition position, PersonalSettings s, ServerLimits limits)
    {
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

        bool? near = null;
        bool NearBuildZone() => near ??= _world.NearBuildZone(player);
        bool SightDecides() => s.VisibleOnly && now >= _sightUnknownUntil && _world.SightAvailable;

        var wanted = new List<(double Dist, SmashTarget Target, SmashKind Kind)>();
        var started = _micros();
        var inReach = _world.Within(_candidates, position, sw.Radius);   // nearest first
        int looked = 0, fresh = 0;
        long inspectMicros = 0, slowest = 0;
        var stopped = false;
        foreach (var e in inReach)
        {
            if (wanted.Count >= SmashRules.MaxTargetsPerStep) break;   // the nearest wanted ones are found
            if (looked > 0 && _micros() - started >= RunBudgetMicros)
            {
                stopped = true;   // the rest waits for the next second
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
            if (!SightDecides() && !HeightWindow.Allows(target.Position.Y - position.Y, s))
            {
                ProbeTrace.Note(e, $"smash: {kind}, outside the height window");
                continue;   // the player's height window
            }
            ProbeTrace.Note(e, $"smash: {kind}, wanted {SmashRules.Wants(kind, sw, NearBuildZone)} (stones {sw.Stones}, " +
                               $"buildables {sw.Buildables}, trees {sw.Trees}, hives {sw.Hives}), may hit {MayHit(target, now)}");
            if (!SmashRules.Wants(kind, sw, NearBuildZone) || !MayHit(target, now)) continue;
            if (SightDecides() && !Visible(player, target, now))
            {
                ProbeTrace.Note(e, $"smash: {kind}, not in plain view");
                continue;
            }
            // This call may have been the first Unknown answer: the fallback applies to this prop already.
            if (!SightDecides() && !HeightWindow.Allows(target.Position.Y - position.Y, s))
            {
                ProbeTrace.Note(e, "smash: sight did not answer, outside the height window fallback");
                continue;
            }
            var dist = target.Position.DistanceTo(position);
            if (dist <= sw.Radius) wanted.Add((dist, target, kind));
        }
        LastRun = new RunWork(inReach.Count, looked, fresh, inspectMicros, slowest, stopped);
        wanted.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var picked = wanted.Take(SmashRules.MaxTargetsPerStep).Select(w => (w.Target, w.Kind));
        foreach (var (target, kind) in picked)
        {
            Remember(target, now);
            _known.Remove(target.Entity.Value);   // a hit target breaks and its id may come back as another: read it anew
            ProbeTrace.Note(target.Entity, $"smash: hit as {kind}");
            _world.Hit(player, target, kind);
            Hit?.Invoke(target, kind, sw);
        }
    }

    public void PlayerLeft(EntityId player) { }   // no per-player state: the tries belong to the targets

    /// <summary>Line of sight from the player to the target, kept <see cref="SightCacheMs"/>; "not known" counts as
    /// visible, and the height window stands in for <see cref="SightRetryMs"/>.</summary>
    private bool Visible(EntityId player, in SmashTarget t, long now)
    {
        var key = (player.Value, t.Entity.Value);
        if (_sight.TryGetValue(key, out var c) && c.Until > now && c.Serial == t.Serial && c.Stamp == t.Stamp) return c.Visible;
        bool visible;
        switch (_world.LineOfSight(player, t.Entity))
        {
            case Sight.Later: return false;   // the step's ray budget is spent: next run
            case Sight.Hidden: _sightUnknownUntil = 0; visible = false; break;
            case Sight.Unknown: _sightUnknownUntil = now + SightRetryMs; visible = true; break;
            default: _sightUnknownUntil = 0; visible = true; break;
        }
        _sight[key] = (now + SightCacheMs, t.Serial, t.Stamp, visible);
        return visible;
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
