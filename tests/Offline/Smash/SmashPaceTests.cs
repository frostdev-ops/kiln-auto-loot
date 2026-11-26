// SPDX-License-Identifier: MIT
// Smash pace (HadToRegister 2026-11-26: at a stack of barrels one breaks, then 45 s to 1.5 min until the next).
// The server's rhythm (LootServer.SmashStep: a new pass once a second per player, the rest of an unfinished pass in the
// next server steps) against a fake world whose line of sight has the game's shared ray budget per server step
// (Kiln.Queries: 64 rays, fewer when rays are slow) and whose props break on the first hit, as in the lab.
// Lab: tests/LabSmash Pace_cellar_below shows the same with the game's own rays.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.Smash;

public sealed class SmashPaceTests
{
    private static readonly Guid TSmash = new("00000000-0000-0000-0000-00000000b001");
    private static readonly Guid TUnknown = new("00000000-0000-0000-0000-00000000bfff");
    private static readonly EntityId Player = new(1);
    private static readonly WorldPosition Origin = new(0, 0, 0);
    private const long StepMs = 16;

    private static SmashTable Table() => SmashTable.Parse(new StringReader($"# test\n{TSmash}\tsmash\tA\n"));

    private sealed class FakeWorld : ISmashWorld
    {
        public readonly Dictionary<EntityId, SmashTarget> Entities = new();
        public readonly List<(long Ms, EntityId Target)> Hits = [];
        public readonly Dictionary<EntityId, Sight> SightOf = new();
        public readonly Dictionary<EntityId, int> SightCalls = new();
        public bool SaveZone;
        /// <summary>Rays the game admits per server step (Kiln.Queries' shared budget); null = no limit.</summary>
        public int? RaysPerStep;
        /// <summary>Rays other code (the loot loop runs first in the step) spends at the start of each step.</summary>
        public int RaysTakenByOthers;
        public long Micros, RayCostMicros, InspectCostMicros;
        public Func<long> Now = () => 0;
        private int _raysThisStep;
        private uint _next = 100;

        public void NewStep() => _raysThisStep = RaysTakenByOthers;

        public EntityId Add(Guid template, double x, double y = 0, double z = 0, Sight sight = Sight.Visible)
        {
            var e = new EntityId(_next++);
            Entities[e] = new SmashTarget(e, template, 1, new WorldPosition(x, y, z), e.Value);
            SightOf[e] = sight;
            return e;
        }

        public IReadOnlyList<EntityId> Candidates() => Entities.Keys.ToList();

        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
            candidates.Where(Entities.ContainsKey).Select(e => Entities[e])
                .Where(t => t.Position.DistanceTo(at) <= radius)
                .OrderBy(t => t.Position.DistanceTo(at)).Select(t => t.Entity).ToList();

        public SmashTarget? Inspect(EntityId entity)
        {
            Micros += InspectCostMicros;
            return Entities.TryGetValue(entity, out var t) ? t : null;
        }

        public long SpawnStamp(EntityId entity) => Entities.TryGetValue(entity, out var t) ? t.Stamp : 0;
        public WorldPosition? PositionOf(EntityId entity) => Entities.TryGetValue(entity, out var t) ? t.Position : null;
        public bool InSaveZone(EntityId player) => SaveZone;
        public bool NearBuildZone(EntityId player) => false;

        /// <summary>A barrel breaks on the first hit (lab: every spawned barrel, crate and urn did).</summary>
        public void Hit(EntityId player, in SmashTarget target, SmashKind kind)
        {
            Hits.Add((Now(), target.Entity));
            Entities.Remove(target.Entity);
        }

        public bool SightAvailable => true;

        public Sight LineOfSight(EntityId player, EntityId target)
        {
            if (RaysPerStep is { } max && _raysThisStep >= max) return Sight.Later;   // BudgetExceeded: no ray cast
            _raysThisStep++;
            Micros += RayCostMicros;
            SightCalls[target] = SightCalls.GetValueOrDefault(target) + 1;
            return SightOf.GetValueOrDefault(target, Sight.Visible);
        }
    }

    private long _now = 1_000_000, _nextPass;
    private readonly FakeWorld _world = new();
    private readonly global::KilnAutoLoot.Server.Smash _smash;

    public SmashPaceTests()
    {
        _world.Now = () => _now;
        _nextPass = _now;
        _smash = new global::KilnAutoLoot.Server.Smash(null!, _world, () => _now, Table, () => _world.Micros);
    }

    private static PersonalSettings S(Action<PersonalSettings>? change = null)
    {
        var s = new PersonalSettings { Radius = 15f };
        change?.Invoke(s);
        return s;
    }

    /// <summary>LootServer.SmashStep for one player over <paramref name="seconds"/> of 16 ms server steps.</summary>
    private void ServerSteps(double seconds, PersonalSettings s, ServerLimits? limits = null, Action? eachStep = null)
    {
        limits ??= new ServerLimits();
        var end = _now + (long)(seconds * 1000);
        for (; _now < end; _now += StepMs)
        {
            _world.NewStep();
            if (_now >= _nextPass)
            {
                _nextPass = _now + 1000;
                _smash.Run(Player, Origin, s, limits);
            }
            else if (_smash.Pending(Player)) _smash.Resume(Player, s, limits);
            eachStep?.Invoke();
        }
    }

    /// <summary>A cellar under the player: two layers of urns out of sight, every one nearer than the barrels in view
    /// (lab Pace_cellar_below: 242 urns, 8 barrels 6.6 m away).</summary>
    private (List<EntityId> Hidden, List<EntityId> Seen) Cellar(int hiddenCount = 242, int seen = 8)
    {
        var hidden = new List<EntityId>();
        for (var i = 0; i < hiddenCount; i++)
            hidden.Add(_world.Add(TSmash, -3 + (i % 11) * 0.6, -4.4 + (i / 121) * 0.6, -3 + (i / 11 % 11) * 0.6, Sight.Hidden));
        var visible = new List<EntityId>();
        for (var i = 0; i < seen; i++)
        {
            var ang = i * Math.PI * 2 / seen;
            visible.Add(_world.Add(TSmash, Math.Cos(ang) * 6.6, 0, Math.Sin(ang) * 6.6));
        }
        Assert.True(hidden.Max(h => _world.Entities[h].Position.DistanceTo(Origin)) < 6.6, "every urn is nearer than the barrels");
        return (hidden, visible);
    }

    [Fact]
    public void Barrels_in_view_behind_many_nearer_props_out_of_sight_break_within_seconds_on_the_game_ray_budget()
    {
        _world.RaysPerStep = 64;
        var (hidden, seen) = Cellar();
        ServerSteps(3, S());
        Assert.All(seen, e => Assert.DoesNotContain(e, _world.Entities.Keys));            // every barrel in view broke
        Assert.DoesNotContain(_world.Hits, h => hidden.Contains(h.Target));                // nothing through the floor
    }

    [Fact]
    public void On_a_slow_pc_with_ten_rays_a_step_and_the_loot_loop_first_barrels_in_view_still_break_within_seconds()
    {
        // HadToRegister's PC: slow rays (Kiln.Queries' 500 us a step = about 10 of them), the loot loop's rays first
        _world.RaysPerStep = 10;
        _world.RaysTakenByOthers = 4;
        _world.RayCostMicros = 50;
        var (hidden, seen) = Cellar(hiddenCount: 120);
        ServerSteps(4, S());
        Assert.All(seen, e => Assert.DoesNotContain(e, _world.Entities.Keys));
        Assert.DoesNotContain(_world.Hits, h => hidden.Contains(h.Target));
    }

    [Fact]
    public void A_room_of_twenty_props_in_view_clears_at_four_a_second()
    {
        _world.RaysPerStep = 64;
        var props = Enumerable.Range(0, 20).Select(i => _world.Add(TSmash, 1 + i * 0.4)).ToList();
        ServerSteps(5, S());
        Assert.Empty(_world.Entities);
        // never more than four hits in any second (the cap per player and second stays)
        foreach (var (ms, _) in _world.Hits)
            Assert.True(_world.Hits.Count(h => h.Ms >= ms && h.Ms < ms + 1000) <= SmashRules.MaxTargetsPerStep);
    }

    [Fact]
    public void A_pass_asks_each_prop_at_most_once_and_keeps_to_the_run_budget_in_every_step()
    {
        _world.RaysPerStep = 64;
        _world.RayCostMicros = 100;
        _world.InspectCostMicros = 20;
        var (hidden, _) = Cellar(hiddenCount: 200, seen: 0);
        long worst = 0;
        var before = _world.Micros;
        ServerSteps(0.9, S(), eachStep: () =>
        {
            worst = Math.Max(worst, _world.Micros - before);
            before = _world.Micros;
        });
        Assert.All(hidden, e => Assert.True(_world.SightCalls.GetValueOrDefault(e) <= 1, $"{e} asked {_world.SightCalls.GetValueOrDefault(e)} times"));
        Assert.All(hidden, e => Assert.Equal(1, _world.SightCalls.GetValueOrDefault(e)));   // and the pass got to all of them
        Assert.True(worst <= global::KilnAutoLoot.Server.Smash.RunBudgetMicros + 100, $"{worst} us in one step");
        Assert.Empty(_world.Hits);
    }

    [Fact]
    public void An_unfinished_pass_stops_when_smashing_goes_off_or_the_player_enters_a_save_zone()
    {
        _world.RaysPerStep = 64;
        Cellar();
        // the first step starts the pass and runs out of rays on the urns; then smashing is switched off
        _world.NewStep();
        _nextPass = _now + 1000;
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        ServerSteps(0.5, S(s => s.Smash = false));
        Assert.Empty(_world.Hits);
        Assert.False(_smash.Pending(Player));

        _now = _nextPass;
        _world.NewStep();
        _nextPass = _now + 1000;
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        _world.SaveZone = true;
        ServerSteps(0.5, S());
        Assert.Empty(_world.Hits);
        Assert.False(_smash.Pending(Player));
    }

    [Fact]
    public void A_player_who_leaves_takes_the_unfinished_pass_along()
    {
        _world.RaysPerStep = 64;
        Cellar();
        _world.NewStep();
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        _smash.PlayerLeft(Player);
        Assert.False(_smash.Pending(Player));
        _smash.Run(Player, Origin, S(), new ServerLimits());
        _smash.Stop();
        Assert.False(_smash.Pending(Player));
    }
}
