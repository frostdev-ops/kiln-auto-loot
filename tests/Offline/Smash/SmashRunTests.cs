// SPDX-License-Identifier: MIT
// Smash.Run's rules against a fake game layer: switches, limits, save zone, build zone, radius, the
// 4-per-step cap and the retry memory.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.Smash;

public sealed class SmashRunTests
{
    private static readonly Guid TSmash = new("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid TShared = new("00000000-0000-0000-0000-00000000a002");
    private static readonly Guid TStone = new("00000000-0000-0000-0000-00000000a003");
    private static readonly Guid TTree = new("00000000-0000-0000-0000-00000000a004");
    private static readonly Guid THive = new("00000000-0000-0000-0000-00000000a005");
    private static readonly Guid TStoneShared = new("00000000-0000-0000-0000-00000000a006");
    private static readonly Guid THiveShared = new("00000000-0000-0000-0000-00000000a007");
    private static readonly Guid TUnknown = new("00000000-0000-0000-0000-00000000afff");

    private static readonly EntityId Player = new(1);
    private static readonly WorldPosition Origin = new(0, 0, 0);

    private static SmashTable Table() => SmashTable.Parse(new StringReader(
        $"# test\n{TSmash}\tsmash\tA\n{TShared}\tshared\tB\n{TStone}\tstone\tC\n{TTree}\ttree\tD\n{THive}\tbeehive\tE\n" +
        $"{TStoneShared}\tstone-shared\tF\n{THiveShared}\tbeehive-shared\tG\n"));

    private sealed class FakeWorld : ISmashWorld
    {
        public readonly List<SmashTarget> Entities = [];
        public readonly List<(EntityId Player, EntityId Target, SmashKind Kind)> Hits = [];
        public bool SaveZone, BuildZone;
        public int CandidateCalls, BuildZoneCalls;
        /// <summary>The work clock (microseconds): each Inspect costs InspectCostMicros.</summary>
        public long Micros, InspectCostMicros;
        public int InspectCalls;

        public EntityId Add(Guid template, double x, ulong serial = 1)
        {
            var e = new EntityId((uint)(100 + Entities.Count));
            Entities.Add(new SmashTarget(e, template, serial, new WorldPosition(x, 0, 0)));
            return e;
        }

        public IReadOnlyList<EntityId> Candidates() { CandidateCalls++; return Entities.Select(t => t.Entity).ToList(); }

        // nearest first, as Nearby.Within
        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
            candidates.Select(Find).Where(t => t is { } x && x.Position.DistanceTo(at) <= radius)
                .OrderBy(t => t!.Value.Position.DistanceTo(at)).Select(t => t!.Value.Entity).ToList();

        public SmashTarget? Inspect(EntityId entity)
        {
            InspectCalls++;
            Micros += InspectCostMicros;
            return Find(entity);
        }

        public WorldPosition? PositionOf(EntityId entity) => Find(entity)?.Position;

        private SmashTarget? Find(EntityId entity)
        {
            foreach (var t in Entities) if (t.Entity == entity) return t;
            return null;
        }

        public bool InSaveZone(EntityId player) => SaveZone;
        public bool NearBuildZone(EntityId player) { BuildZoneCalls++; return BuildZone; }
        public void Hit(EntityId player, in SmashTarget target, SmashKind kind) => Hits.Add((player, target.Entity, kind));
        public void Despawn(EntityId e) => Entities.RemoveAll(t => t.Entity == e);
    }

    private long _now = 1_000_000;
    private readonly FakeWorld _world = new();
    private readonly global::KilnAutoLoot.Server.Smash _smash;

    public SmashRunTests() => _smash = new global::KilnAutoLoot.Server.Smash(null!, _world, () => _now, Table, () => _world.Micros);

    // ---- the run budget (HadToRegister 2026-11-20: a 3.6 s step on arriving in a village)

    [Fact]
    public void A_village_of_2000_props_new_to_the_mod_is_looked_at_within_the_budget_per_run()
    {
        const long cost = 500;
        _world.InspectCostMicros = cost;
        for (var i = 0; i < 2000; i++) _world.Add(TUnknown, 1 + (i % 140) / 10.0);   // nothing to hit: every one is looked at
        var before = _world.Micros;
        Run(S());
        Assert.True(_world.Micros - before <= global::KilnAutoLoot.Server.Smash.RunBudgetMicros + cost, $"{_world.Micros - before} us");
        var looked = _world.InspectCalls;
        Assert.True(looked < 2000);
        // later runs go on with the rest, and what was read once is not read again
        for (var i = 0; i < 400 && _world.InspectCalls < 2000; i++) { _now += 1000; Run(S()); }
        Assert.Equal(2000, _world.InspectCalls);
    }

    [Fact]
    public void A_run_stops_looking_once_it_has_the_four_nearest_wanted_targets()
    {
        for (var i = 0; i < 200; i++) _world.Add(TSmash, 1 + i * 0.05);
        Run(S());
        Assert.Equal(4, _world.Hits.Count);
        Assert.Equal(4, _world.InspectCalls);
        Assert.Equal(_world.Entities.Take(4).Select(t => t.Entity), _world.Hits.Select(h => h.Target));
    }

    [Fact]
    public void A_target_read_once_is_not_read_again_while_it_stays_in_reach()
    {
        _world.Add(TUnknown, 2);
        _world.Add(TUnknown, 3);
        Run(S());
        for (var i = 0; i < 5; i++) { _now += 1000; Run(S()); }
        Assert.Equal(2, _world.InspectCalls);
    }

    private static PersonalSettings S(Action<PersonalSettings>? change = null)
    {
        var s = new PersonalSettings { Radius = 15f };
        change?.Invoke(s);
        return s;
    }

    private void Run(PersonalSettings s, ServerLimits? limits = null) => _smash.Run(Player, Origin, s, limits ?? new ServerLimits());

    private bool HitOnce(Guid template, PersonalSettings s, ServerLimits? limits = null)
    {
        var e = _world.Add(template, 3);
        Run(s, limits);
        return _world.Hits.Any(h => h.Target == e);
    }

    [Fact] public void Smash_kind_is_hit() => Assert.True(HitOnce(TSmash, S()));

    [Fact] public void Unknown_template_is_never_hit() => Assert.False(HitOnce(TUnknown, S()));

    [Fact] public void Auto_smash_off_hits_nothing_trees_and_hives_included()
    {
        var s = S(x => { x.Smash = false; x.FellTrees = true; x.SmashHives = true; });
        _world.Add(TSmash, 2); _world.Add(TTree, 3); _world.Add(THive, 4);
        Run(s);
        Assert.Empty(_world.Hits);
    }

    [Fact] public void Auto_loot_off_F9_hits_nothing_trees_and_hives_included()
    {
        // F9 is the master switch: with auto loot off nothing is smashed, though "Smash things" stays on
        var s = S(x => { x.Looting = false; x.FellTrees = true; x.SmashHives = true; });
        _world.Add(TSmash, 2); _world.Add(TTree, 3); _world.Add(THive, 4); _world.Add(TStone, 5); _world.Add(TShared, 6);
        Run(s);
        Assert.Empty(_world.Hits);
        Assert.True(SmashSwitches.Of(s, new ServerLimits()).Off);
        // on again: smashing comes back with the same switches
        s.Looting = true;
        Run(s);
        Assert.NotEmpty(_world.Hits);
    }

    [Fact] public void Server_forbids_smashing() =>
        Assert.False(HitOnce(TSmash, S(), new ServerLimits { AllowSmash = false }));

    [Fact] public void Nothing_in_a_save_zone()
    {
        _world.SaveZone = true;
        Assert.False(HitOnce(TSmash, S()));
    }

    [Fact] public void World_props_are_hit_near_a_build_zone()
    {
        _world.BuildZone = true;
        Assert.True(HitOnce(TSmash, S()));
    }

    [Fact] public void Shared_only_away_from_build_zones()
    {
        Assert.True(HitOnce(TShared, S()));
        _world.Hits.Clear(); _world.Entities.Clear();
        _world.BuildZone = true;
        Assert.False(HitOnce(TShared, S()));
    }

    [Fact] public void Shared_off_when_buildable_smashing_is_off() =>
        Assert.False(HitOnce(TShared, S(x => x.SmashBuildables = false)));

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void Stones_need_both_collect_stones_and_ground_loot(bool stones, bool groundLoot, bool hit)
    {
        Assert.Equal(hit, HitOnce(TStone, S(x => { x.Stones = stones; x.GroundLoot = groundLoot; })));
    }

    [Fact] public void Shared_stones_need_both_switches_and_no_build_zone()
    {
        Assert.True(HitOnce(TStoneShared, S()));
        _world.Hits.Clear(); _world.Entities.Clear();
        Assert.False(HitOnce(TStoneShared, S(x => x.SmashBuildables = false)));
        _world.Hits.Clear(); _world.Entities.Clear();
        _world.BuildZone = true;
        Assert.False(HitOnce(TStoneShared, S()));
    }

    [Fact] public void Trees_only_with_smash_trees_and_away_from_build_zones()
    {
        Assert.False(HitOnce(TTree, S()));
        _world.Hits.Clear(); _world.Entities.Clear();
        Assert.True(HitOnce(TTree, S(x => x.FellTrees = true)));
        _world.Hits.Clear(); _world.Entities.Clear();
        Assert.False(HitOnce(TTree, S(x => x.FellTrees = true), new ServerLimits { AllowFelling = false }));
        _world.Hits.Clear(); _world.Entities.Clear();
        _world.BuildZone = true;
        Assert.False(HitOnce(TTree, S(x => x.FellTrees = true)));
    }

    [Fact] public void Hives_with_smash_beehives_are_hit_like_props_even_near_a_build_zone()
    {
        _world.BuildZone = true;
        Assert.True(HitOnce(THive, S(x => x.SmashHives = true)));
    }

    [Fact] public void Hives_with_only_smash_trees_fall_with_the_trees()
    {
        Assert.True(HitOnce(THive, S(x => x.FellTrees = true)));
        _world.Hits.Clear(); _world.Entities.Clear();
        _world.BuildZone = true;
        Assert.False(HitOnce(THive, S(x => x.FellTrees = true)));
    }

    [Fact] public void Hives_need_one_of_the_two_switches()
    {
        Assert.False(HitOnce(THive, S()));
        _world.Hits.Clear(); _world.Entities.Clear();
        Assert.False(HitOnce(THive, S(x => x.SmashHives = true), new ServerLimits { AllowHives = false }));
    }

    [Fact] public void Buildable_hives_with_smash_beehives_are_shared()
    {
        Assert.True(HitOnce(THiveShared, S(x => x.SmashHives = true)));
        _world.Hits.Clear(); _world.Entities.Clear();
        _world.BuildZone = true;
        Assert.False(HitOnce(THiveShared, S(x => x.SmashHives = true)));
    }

    [Fact] public void Radius_is_the_general_radius_capped_by_the_server()
    {
        var near = _world.Add(TSmash, 9);
        var far = _world.Add(TSmash, 14);
        Run(S(), new ServerLimits { MaxRadius = 10 });
        Assert.Contains(_world.Hits, h => h.Target == near);
        Assert.DoesNotContain(_world.Hits, h => h.Target == far);
    }

    [Fact] public void Outside_the_radius_is_not_hit()
    {
        _world.Add(TSmash, 16);
        Run(S());
        Assert.Empty(_world.Hits);
    }

    [Fact] public void At_most_four_per_step_nearest_first()
    {
        var ids = new[] { 9.0, 1, 7, 3, 5, 2 }.Select(x => (x, e: _world.Add(TSmash, x))).ToList();
        Run(S());
        Assert.Equal(4, _world.Hits.Count);
        var expected = ids.OrderBy(i => i.x).Take(4).Select(i => i.e).ToHashSet();
        Assert.True(expected.SetEquals(_world.Hits.Select(h => h.Target)));
        // the next second takes the rest
        _now += 1000;
        _world.Hits.Clear();
        Run(S());
        Assert.Equal(2, _world.Hits.Count);
    }

    [Fact] public void A_target_is_not_hit_again_while_it_breaks_and_given_up_after_three_tries()
    {
        var e = _world.Add(TSmash, 2);
        Run(S());
        _now += 1000; Run(S());                                   // 1 s later: still within RetryMs
        Assert.Single(_world.Hits);
        _now += 600; Run(S());                                    // 1.6 s after the first: again
        _now += 1600; Run(S());                                   // third try
        Assert.Equal(3, _world.Hits.Count);
        _now += 1600; Run(S());                                   // given up
        _now += 20_000; Run(S());
        Assert.Equal(3, _world.Hits.Count);
        _now += 10_000; Run(S());                                 // 30 s later it gets another round
        Assert.Equal(4, _world.Hits.Count);
        Assert.All(_world.Hits, h => Assert.Equal(e, h.Target));
    }

    [Fact] public void A_reused_entity_id_is_a_new_target()
    {
        var e = _world.Add(TSmash, 2, serial: 1);
        Run(S());
        _world.Despawn(e);
        _world.Entities.Add(new SmashTarget(e, TSmash, 2, new WorldPosition(2, 0, 0)));
        _now += 100; Run(S());
        Assert.Equal(2, _world.Hits.Count);
    }

    [Fact] public void The_candidate_list_is_shared_by_players_in_the_same_moment()
    {
        _world.Add(TSmash, 2);
        _smash.Run(Player, Origin, S(), new ServerLimits());
        _smash.Run(new EntityId(2), Origin, S(), new ServerLimits());
        Assert.Equal(1, _world.CandidateCalls);
        _now += global::KilnAutoLoot.Server.Smash.CandidateCacheMs;
        _smash.Run(Player, Origin, S(), new ServerLimits());
        Assert.Equal(2, _world.CandidateCalls);
    }

    [Fact] public void The_build_zone_is_asked_only_when_a_target_needs_it()
    {
        _world.Add(TSmash, 2);
        Run(S());
        Assert.Equal(0, _world.BuildZoneCalls);
        _world.Add(TShared, 3); _world.Add(TTree, 4);
        _now += 2000;
        Run(S(x => x.FellTrees = true));
        Assert.Equal(1, _world.BuildZoneCalls);
    }

    [Fact] public void Stop_forgets_the_tries()
    {
        _world.Add(TSmash, 2);
        Run(S());
        _smash.Stop();
        Run(S());
        Assert.Equal(2, _world.Hits.Count);
    }
}
