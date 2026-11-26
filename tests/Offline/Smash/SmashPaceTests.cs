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

        public void NewStep()
        {
            _raysThisStep = RaysTakenByOthers;
            RayMicrosThisStep = 0;
        }

        /// <summary>Rays smashing cast in this step (the others' not counted), and their time.</summary>
        public int SmashRaysThisStep => _raysThisStep - RaysTakenByOthers;
        public long RayMicrosThisStep;
        /// <summary>Rays the game would still admit in this step (for a mod that asks after Auto Loot).</summary>
        public int RaysLeft => RaysPerStep is { } max ? Math.Max(0, max - _raysThisStep) : int.MaxValue;

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

        /// <summary>A target's answer at one height above its origin (a ceiling over a stack, a lintel over a niche);
        /// otherwise SightOf for every height.</summary>
        public readonly Dictionary<(EntityId, double), Sight> SightAt = new();

        public Sight LineOfSight(EntityId player, EntityId target, double toHeight)
        {
            if (RaysPerStep is { } max && _raysThisStep >= max) return Sight.Later;   // BudgetExceeded: no ray cast
            _raysThisStep++;
            Micros += RayCostMicros;
            RayMicrosThisStep += RayCostMicros;
            SightCalls[target] = SightCalls.GetValueOrDefault(target) + 1;
            return SightAt.TryGetValue((target, toHeight), out var at) ? at : SightOf.GetValueOrDefault(target, Sight.Visible);
        }
    }

    private long _now = 1_000_000, _nextPass;
    private long _stepMs = StepMs;
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
        for (; _now < end; _now += _stepMs)
        {
            _world.NewStep();
            _smash.BeginStep(1);
            if (_now >= _nextPass)
            {
                _nextPass = _now + 1000;
                _smash.Run(Player, Origin, s, limits);
            }
            else if (_smash.Pending(Player)) _smash.Resume(Player, Origin, s, limits);
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
    public void A_pass_looks_at_each_prop_once_and_keeps_to_the_run_budget_in_every_step()
    {
        _world.RaysPerStep = 64;
        _world.RayCostMicros = 5;
        _world.InspectCostMicros = 600;   // the step's time runs out on reading new templates (a village after a fast travel)
        var (hidden, _) = Cellar(hiddenCount: 200, seen: 0);
        long worst = 0;
        var before = _world.Micros;
        ServerSteps(0.9, S(), eachStep: () =>
        {
            worst = Math.Max(worst, _world.Micros - before);
            before = _world.Micros;
        });
        // one look each, and the pass got to all of them: a hidden prop is two rays (its body, then the point above)
        Assert.All(hidden, e => Assert.Equal(2, _world.SightCalls.GetValueOrDefault(e)));
        // at most one look (a template read and two rays) beyond the budget in any step
        Assert.True(worst <= global::KilnAutoLoot.Server.Smash.RunBudgetMicros + 600 + 2 * 5, $"{worst} us in one step");
        Assert.Empty(_world.Hits);
    }

    [Fact]
    public void An_unfinished_pass_stops_when_smashing_goes_off_or_the_player_enters_a_save_zone()
    {
        _world.RaysPerStep = 64;
        Cellar();
        // the first step starts the pass and runs out of rays on the urns; then smashing is switched off
        _world.NewStep();
        _smash.BeginStep(1);
        _nextPass = _now + 1000;
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        ServerSteps(0.5, S(s => s.Smash = false));
        Assert.Empty(_world.Hits);
        Assert.False(_smash.Pending(Player));

        _now = _nextPass;
        _world.NewStep();
        _smash.BeginStep(1);
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
        _smash.BeginStep(1);
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        _smash.PlayerLeft(Player);
        Assert.False(_smash.Pending(Player));
        _smash.Run(Player, Origin, S(), new ServerLimits());
        _smash.Stop();
        Assert.False(_smash.Pending(Player));
    }
    // ---- a check that does not answer (NotLoaded) is that prop's alone (lab Pace_built_room / Pace_cellar_below: one
    //      such answer turned line of sight off for every prop for 5 s, and props behind walls and under floors broke)

    [Fact]
    public void One_check_that_does_not_answer_lets_the_height_window_judge_that_prop_only_never_the_ones_out_of_sight()
    {
        _world.RaysPerStep = 64;
        var unknown = _world.Add(TSmash, 1.0, sight: Sight.Unknown);          // its ray does not answer (NotLoaded)
        var hidden = Enumerable.Range(0, 10).Select(i => _world.Add(TSmash, 2 + i * 0.3, sight: Sight.Hidden)).ToList();
        var seen = Enumerable.Range(0, 4).Select(i => _world.Add(TSmash, 6 + i * 0.3)).ToList();
        ServerSteps(6, S());
        Assert.DoesNotContain(_world.Hits, h => hidden.Contains(h.Target));   // never through walls
        Assert.Contains(_world.Hits, h => h.Target == unknown);               // the height window (no limit) lets it be hit
        Assert.All(seen, e => Assert.DoesNotContain(e, _world.Entities.Keys));
    }

    [Fact]
    public void A_prop_whose_check_does_not_answer_is_left_when_the_height_window_says_no_and_asked_again_later()
    {
        _world.RaysPerStep = 64;
        var below = _world.Add(TSmash, 2, y: -6, sight: Sight.Unknown);
        var seen = _world.Add(TSmash, 3, y: -6);                                // in view, as deep: line of sight decides it
        ServerSteps(1, S(s => s.HeightBelow = 4));
        Assert.Contains(_world.Hits, h => h.Target == seen);
        Assert.DoesNotContain(_world.Hits, h => h.Target == below);
        Assert.Equal(1, _world.SightCalls[below]);
        _world.SightOf[below] = Sight.Visible;                                  // its tile is in now
        ServerSteps(global::KilnAutoLoot.Server.Smash.SightRetryMs / 1000.0 + 1, S(s => s.HeightBelow = 4));
        Assert.Contains(_world.Hits, h => h.Target == below);
        Assert.Equal(2, _world.SightCalls[below]);                              // once, then once after SightRetryMs
    }
    // ---- where line of sight looks (lab Pace_built_room: under a ceiling the game's rays put about 2 m up, the barrels
    //      on the stacks and on the shelf were "hidden" though in plain view: the point 1.5 m above each one's origin,
    //      which was all a check looked at, lay in the ceiling)

    private const double Body = 0.6, Above = 1.5;   // the prop itself, and the point the check used alone before 0.2.12

    [Fact]
    public void Barrels_on_a_stack_and_a_shelf_under_a_low_ceiling_are_seen_at_their_own_height()
    {
        _world.RaysPerStep = 64;
        var floor = _world.Add(TSmash, 3);
        var stacked = _world.Add(TSmash, 4, y: 1.05);
        var top = _world.Add(TSmash, 4, y: 2.1);
        var shelf = _world.Add(TSmash, 5, y: 1.0);
        foreach (var e in new[] { stacked, top, shelf }) _world.SightAt[(e, Above)] = Sight.Hidden;   // in the ceiling
        ServerSteps(1.5, S());
        Assert.All(new[] { floor, stacked, top, shelf }, e => Assert.Contains(_world.Hits, h => h.Target == e));
    }

    [Fact]
    public void A_half_buried_urn_is_still_seen_by_the_point_above_it_and_a_barrel_behind_a_wall_by_neither()
    {
        _world.RaysPerStep = 64;
        var buried = _world.Add(TSmash, 3, y: -0.4);
        _world.SightAt[(buried, Body)] = Sight.Hidden;    // its body is in the rubble, the point above it is clear
        var walled = _world.Add(TSmash, 4, sight: Sight.Hidden);
        ServerSteps(3, S());
        Assert.Contains(_world.Hits, h => h.Target == buried);
        Assert.DoesNotContain(_world.Hits, h => h.Target == walled);
    }

    // ---- review of 0.2.12: a pass that goes on across server steps must leave the rest of every step to the others.
    //      Kiln.Queries: every mod's rays and every creature's sight share 64 rays and 500 us a step; a dedicated server
    //      steps at 24.5 Hz. Before the review a pass took every ray the game had left, step after step, while props in
    //      reach were unanswered, so a mod (or a creature brain) asking after Auto Loot got none in those steps.

    private const int RayShare = global::KilnAutoLoot.Server.Smash.MaxRaysPerStep;              // a quarter of the game's 64
    private const long RayTimeShare = global::KilnAutoLoot.Server.Smash.MaxRayMicrosPerStep;    // a quarter of the game's 500 us
    private const long DedicatedStepMs = 41;    // 24.5 Hz

    /// <summary>LootServer.SmashStep for several players: a pass a second for each, the rest in the next steps, one
    /// step's smash time and rays shared, the players taking turns going first.</summary>
    private void ServerStepsFor(double seconds, IReadOnlyList<(EntityId Player, Func<WorldPosition> At)> players,
        PersonalSettings s, Action? eachStep = null)
    {
        var limits = new ServerLimits();
        var next = players.ToDictionary(p => p.Player, _ => _now);
        var end = _now + (long)(seconds * 1000);
        for (; _now < end; _now += _stepMs)
        {
            _world.NewStep();
            var first = _smash.BeginStep(players.Count);   // as LootServer.SmashStep: the players take turns going first
            for (var i = 0; i < players.Count; i++)
            {
                var (player, at) = players[(first + i) % players.Count];
                if (_now >= next[player])
                {
                    next[player] = _now + 1000;
                    _smash.Run(player, at(), s, limits);
                }
                else if (_smash.Pending(player)) _smash.Resume(player, at(), s, limits);
            }
            eachStep?.Invoke();
        }
    }

    /// <summary>A cellar of <paramref name="hiddenCount"/> urns out of sight under <paramref name="at"/> (every one
    /// nearer than the barrels) and <paramref name="seen"/> barrels in view 6.6 m away.</summary>
    private (List<EntityId> Hidden, List<EntityId> Seen) CellarAt(WorldPosition at, int hiddenCount, int seen)
    {
        var hidden = new List<EntityId>();
        for (var i = 0; i < hiddenCount; i++)
            hidden.Add(_world.Add(TSmash, at.X - 3 + (i % 11) * 0.6, at.Y - 4.4 + (i / 121) * 0.3, at.Z - 3 + (i / 11 % 11) * 0.6, Sight.Hidden));
        var visible = new List<EntityId>();
        for (var i = 0; i < seen; i++)
        {
            var ang = i * Math.PI * 2 / seen;
            visible.Add(_world.Add(TSmash, at.X + Math.Cos(ang) * 6.6, at.Y, at.Z + Math.Sin(ang) * 6.6));
        }
        return (hidden, visible);
    }

    [Fact]
    public void On_a_dedicated_server_smashing_casts_at_most_a_quarter_of_the_game_rays_a_step_and_still_reaches_the_barrels()
    {
        _stepMs = DedicatedStepMs;
        _world.RaysPerStep = 64;
        var (hidden, seen) = Cellar();
        int most = 0, othersShort = 0;
        ServerSteps(4, S(), eachStep: () =>
        {
            most = Math.Max(most, _world.SmashRaysThisStep);
            if (_world.RaysLeft < 64 - RayShare) othersShort++;   // a mod asking after Auto Loot found less than 48
        });
        Assert.True(most <= RayShare, $"smashing cast {most} rays in one step");
        Assert.Equal(0, othersShort);
        Assert.All(seen, e => Assert.DoesNotContain(e, _world.Entities.Keys));   // all 8 barrels in view within 4 s
        Assert.DoesNotContain(_world.Hits, h => hidden.Contains(h.Target));
    }

    [Fact]
    public void On_slow_rays_smashing_keeps_to_a_quarter_of_the_step_ray_time()
    {
        _stepMs = DedicatedStepMs;
        _world.RaysPerStep = 64;
        _world.RayCostMicros = 50;   // a slow PC: the game's 500 us hold ten of these
        Cellar(hiddenCount: 120);
        long most = 0;
        ServerSteps(3, S(), eachStep: () => most = Math.Max(most, _world.RayMicrosThisStep));
        // at most one look (two rays) beyond the share
        Assert.True(most <= RayTimeShare + 2 * 50, $"smashing spent {most} us on rays in one step");
    }

    [Fact]
    public void Players_share_one_step_budget_and_take_turns_so_a_ruin_with_many_props_does_not_starve_another_player()
    {
        _stepMs = DedicatedStepMs;
        _world.RaysPerStep = 64;
        _world.InspectCostMicros = 20;
        _world.RayCostMicros = 2;
        // player 1 stands over a huge field of props out of sight: more than the game's whole budget could answer in
        // time, so its pass always wants rays
        var p1 = new EntityId(1);
        var p1At = new WorldPosition(0, 0, 0);
        var field = new List<EntityId>();
        for (var i = 0; i < 1500; i++)
            field.Add(_world.Add(TSmash, -6 + (i % 20) * 0.6, -2 - (i / 400) * 0.5, -6 + (i / 20 % 20) * 0.6, Sight.Hidden));
        // player 2, 200 m away, at a small cellar with barrels in view
        var p2 = new EntityId(2);
        var p2At = new WorldPosition(200, 0, 0);
        var (hidden2, seen2) = CellarAt(p2At, hiddenCount: 40, seen: 4);
        int most = 0;
        long mostMicros = 0, before = _world.Micros;
        long? p2Done = null;
        var start = _now;
        ServerStepsFor(3, [(p1, () => p1At), (p2, () => p2At)], S(), () =>
        {
            most = Math.Max(most, _world.SmashRaysThisStep);
            mostMicros = Math.Max(mostMicros, _world.Micros - before);
            before = _world.Micros;
            if (p2Done is null && seen2.All(e => !_world.Entities.ContainsKey(e))) p2Done = _now - start;
        });
        Assert.True(most <= RayShare, $"{most} rays in one step, both players together");
        Assert.True(mostMicros <= global::KilnAutoLoot.Server.Smash.RunBudgetMicros + 2 * 2 + 20, $"{mostMicros} us in one step, both players together");
        Assert.True(p2Done is not null && p2Done <= 2000, $"player 2's barrels done at {p2Done} ms");
        Assert.DoesNotContain(_world.Hits, h => field.Contains(h.Target) || hidden2.Contains(h.Target));
    }

    [Fact]
    public void A_pass_that_goes_on_after_the_player_moved_judges_reach_from_where_the_player_is_now()
    {
        _world.RaysPerStep = 16;   // the others took the rest: the pass needs several steps for the urns
        var at = Origin;
        var hidden = Enumerable.Range(0, 60).Select(i => _world.Add(TSmash, 0.5 + i * 0.07, sight: Sight.Hidden)).ToList();
        var barrels = Enumerable.Range(0, 4).Select(i => _world.Add(TSmash, 8 + i * 0.5)).ToList();
        _world.NewStep();
        _smash.BeginStep(1);
        _nextPass = _now + 1000;
        _smash.Run(Player, at, S(), new ServerLimits());
        Assert.True(_smash.Pending(Player));
        at = new WorldPosition(-30, 0, 0);   // glided away: the barrels are 38 m off now, out of the 15 m radius
        for (var i = 0; i < 30 && _smash.Pending(Player); i++)
        {
            _now += StepMs;
            _world.NewStep();
            _smash.BeginStep(1);
            _smash.Resume(Player, at, S(), new ServerLimits());
        }
        Assert.Empty(_world.Hits);
    }
}
