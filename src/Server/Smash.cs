// SPDX-License-Identifier: MIT
// Smashing props, felling trees, smashing bee hives around each player once per second.
// LootServer calls Run once per second per player on the tick thread.
//
// C# picks the targets (the template's kind from Data.Smash, read from the game, the switches, the player's two states) and runs a
// one-target program per hit (SmashHit). Because the loop runs in C#:
//   - at most SmashRules.MaxTargetsPerStep (4) targets per player per call, nearest first;
//   - a target is hit again only after RetryMs (it needs a step or two to break), and after MaxTries hits that did
//     not break it, it is left alone for GiveUpMs (otherwise one target that never breaks would hold a slot forever).
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class Smash
{
    public const long CandidateCacheMs = 250, RetryMs = 1500, GiveUpMs = 30_000;
    public const int MaxTries = 3;

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
        public int Count;
        public long LastMs;
        public long IgnoreUntilMs;
    }

    public Smash(Data data) : this(data, new KilnSmashWorld(), () => Environment.TickCount64, () => data.Smash) { }

    internal Smash(Data data, ISmashWorld world, Func<long> nowMs, Func<SmashTable> table)
    {
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
    }

    /// <summary>Once per second per player, tick thread. Does nothing when auto loot (s.Looting, F9) or s.Smash is off or the limits forbid it. Only
    /// targets inside the player's height window (heightAbove / heightBelow) are hit.</summary>
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

        var wanted = new List<(double Dist, SmashTarget Target, SmashKind Kind)>();
        foreach (var e in _world.Within(_candidates, position, sw.Radius))
        {
            if (_world.Inspect(e) is not { } target || !table.TryGet(target.Template, out var kind))
            {
                ProbeTrace.Note(e, "smash: not a smash target");
                continue;
            }
            if (!HeightWindow.Allows(target.Position.Y - position.Y, s))
            {
                ProbeTrace.Note(e, $"smash: {kind}, outside the height window");
                continue;   // the player's height window
            }
            ProbeTrace.Note(e, $"smash: {kind}, wanted {SmashRules.Wants(kind, sw, NearBuildZone)} (stones {sw.Stones}, " +
                               $"buildables {sw.Buildables}, trees {sw.Trees}, hives {sw.Hives}), may hit {MayHit(target, now)}");
            if (!SmashRules.Wants(kind, sw, NearBuildZone) || !MayHit(target, now)) continue;
            var dist = target.Position.DistanceTo(position);
            if (dist <= sw.Radius) wanted.Add((dist, target, kind));
        }
        wanted.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        var picked = wanted.Take(SmashRules.MaxTargetsPerStep).Select(w => (w.Target, w.Kind));
        foreach (var (target, kind) in picked)
        {
            Remember(target, now);
            ProbeTrace.Note(target.Entity, $"smash: hit as {kind}");
            _world.Hit(player, target, kind);
            Hit?.Invoke(target, kind, sw);
        }
    }

    public void PlayerLeft(EntityId player) { }   // no per-player state: the tries belong to the targets

    private bool MayHit(in SmashTarget t, long now)
    {
        if (!_tries.TryGetValue(t.Entity.Value, out var tr) || tr.Serial != t.Serial) return true;
        if (now < tr.IgnoreUntilMs) return false;
        return now - tr.LastMs >= RetryMs;
    }

    private void Remember(in SmashTarget t, long now)
    {
        if (!_tries.TryGetValue(t.Entity.Value, out var tr) || tr.Serial != t.Serial)
            _tries[t.Entity.Value] = tr = new Tries { Serial = t.Serial };
        if (tr.IgnoreUntilMs != 0 && now >= tr.IgnoreUntilMs) { tr.Count = 0; tr.IgnoreUntilMs = 0; }
        tr.Count++;
        tr.LastMs = now;
        if (tr.Count >= MaxTries) tr.IgnoreUntilMs = now + GiveUpMs;
    }

    private void PruneTries(long now)
    {
        if (now - _lastPrune < 10_000) return;
        _lastPrune = now;
        List<uint>? gone = null;
        foreach (var (id, tr) in _tries)
            if (now - tr.LastMs > GiveUpMs * 2 && now >= tr.IgnoreUntilMs) (gone ??= []).Add(id);
        if (gone is not null) foreach (var id in gone) _tries.Remove(id);
    }
}
