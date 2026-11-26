// SPDX-License-Identifier: MIT
// "Smash things around me" off (or the World tab's "Smashing allowed" off) breaks nothing, whatever the switches under it
// say, and a change made while the world runs counts from the next once-a-second run: the host's window (SetPersonal), a
// joined player's message (QueuePersonal, applied on the tick) and the World tab (the server's limits, changed in place
// as Kiln changes a config). Smash.Run gets what LootServer.SmashStep hands it: the player's settings in effect and the
// server's limits. (HadToRegister 2026-11-25: things kept breaking with both switches off, also after a restart; his
// kiln-auto-loot.log showed smash 0 on the server and no smash hit at all, so these pin down that Auto Loot's own
// smashing is off then.)
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Offline.LootServerTests;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.Smash;

public sealed class SmashOffTests
{
    private static readonly SmashKind[] Kinds =
        [SmashKind.Smash, SmashKind.Shared, SmashKind.Stone, SmashKind.StoneShared, SmashKind.Tree, SmashKind.Beehive, SmashKind.BeehiveShared];

    private static Guid TemplateOf(SmashKind kind) => new($"00000000-0000-0000-0000-0000000b{(int)kind:x4}");

    private static SmashTable Table() => SmashTable.Parse(new StringReader(
        string.Concat(Kinds.Select(k => $"{TemplateOf(k)}\t{SmashTable.WordOf(k)}\t{k}\n"))));

    /// <summary>One target of every kind within 3 m of the player; no save zone, no build zone, no line of sight.</summary>
    private sealed class World : ISmashWorld
    {
        private readonly List<SmashTarget> _targets = [];
        public readonly List<SmashKind> Hits = [];

        public World()
        {
            for (var i = 0; i < Kinds.Length; i++)   // nearest first in the order of Kinds
                _targets.Add(new SmashTarget(new EntityId((uint)(500 + i)), TemplateOf(Kinds[i]), 1, new WorldPosition(1 + i * 0.3, 0, 0), 1));
        }

        public IReadOnlyList<EntityId> Candidates() => _targets.Select(t => t.Entity).ToList();
        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
            _targets.Where(t => candidates.Contains(t.Entity) && t.Position.DistanceTo(at) <= radius)
                .OrderBy(t => t.Position.DistanceTo(at)).Select(t => t.Entity).ToList();
        public SmashTarget? Inspect(EntityId entity) => Find(entity);
        public long SpawnStamp(EntityId entity) => 1;
        public WorldPosition? PositionOf(EntityId entity) => Find(entity)?.Position;
        private SmashTarget? Find(EntityId entity)
        {
            foreach (var t in _targets) if (t.Entity == entity) return t;
            return null;
        }
        public bool InSaveZone(EntityId player) => false;
        public bool NearBuildZone(EntityId player) => false;
        public void Hit(EntityId player, in SmashTarget target, SmashKind kind) => Hits.Add(kind);
        public bool SightAvailable => false;
        public Sight LineOfSight(EntityId player, EntityId target, double toHeight) => Sight.Unknown;
    }

    private long _now = 1_000_000;
    private World _world = new();
    private global::KilnAutoLoot.Server.Smash _smash;

    public SmashOffTests() => _smash = NewSmash();

    private global::KilnAutoLoot.Server.Smash NewSmash() =>
        new(null!, _world, () => _now, Table);

    /// <summary>Every switch under "Smash things around me" on: furniture, stones (with things on the ground), trees, hives.</summary>
    private static void AllOn(PersonalSettings s)
    {
        s.Looting = true;
        s.Radius = 15f;
        s.Smash = true;
        s.SmashBuildables = true;
        s.GroundLoot = true;
        s.Stones = true;
        s.FellTrees = true;
        s.SmashHives = true;
    }

    /// <summary>One second later, one run with what SmashStep hands over; returns the kinds hit in it.</summary>
    private List<SmashKind> RunOnce(PersonalSettings inEffect, ServerLimits limits)
    {
        _now += 1000;
        _world.Hits.Clear();
        _smash.Run(new EntityId(1), new WorldPosition(0, 0, 0), inEffect, limits);
        return [.. _world.Hits];
    }

    /// <summary>The kinds a fresh world (no retry memory) hits in one run.</summary>
    private List<SmashKind> FreshRun(PersonalSettings inEffect, ServerLimits limits)
    {
        _world = new World();
        _smash = NewSmash();
        return RunOnce(inEffect, limits);
    }

    [Fact]
    public void Every_kind_is_hit_while_everything_is_on()
    {
        var s = new PersonalSettings();
        AllOn(s);
        // at most four per run, nearest first
        var hit = FreshRun(SettingsRules.Clamp(s, new ServerLimits()), new ServerLimits());
        Assert.Equal(SmashRules.MaxTargetsPerStep, hit.Count);
        Assert.Equal(Kinds.Take(4), hit);
    }

    [Fact]
    public void Smash_off_breaks_nothing_whatever_the_switches_under_it_say()
    {
        var s = new PersonalSettings();
        AllOn(s);
        s.Smash = false;
        Assert.Empty(FreshRun(SettingsRules.Clamp(s, new ServerLimits()), new ServerLimits()));
        // also when the clamp is skipped (the raw settings): Smash.Run's own gate
        Assert.Empty(FreshRun(s, new ServerLimits()));
    }

    [Fact]
    public void Smashing_not_allowed_on_the_World_tab_breaks_nothing_for_anyone()
    {
        var s = new PersonalSettings();
        AllOn(s);
        var limits = new ServerLimits { AllowSmash = false };
        Assert.Empty(FreshRun(SettingsRules.Clamp(s, limits), limits));
        Assert.Empty(FreshRun(s, limits));   // the player's own smash left on: the limit still decides
    }

    [Fact]
    public void Auto_loot_off_breaks_nothing()
    {
        var s = new PersonalSettings();
        AllOn(s);
        s.Looting = false;
        Assert.Empty(FreshRun(SettingsRules.Clamp(s, new ServerLimits()), new ServerLimits()));
    }

    [Fact]
    public void The_hosts_window_turning_smash_off_and_on_counts_from_the_next_run()
    {
        var rig = new LoopRig(AllOn);
        var server = new ServerSettings();
        rig.Loop.Configure(server);
        Assert.NotEmpty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));

        // LootServer.SetLocalPersonal: the host's window hands the settings over in the same process
        var off = new PersonalSettings();
        AllOn(off);
        off.Smash = false;
        rig.Loop.SetPersonal(rig.Player, off);
        for (var i = 0; i < 5; i++) Assert.Empty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));

        var on = new PersonalSettings();
        AllOn(on);
        rig.Loop.SetPersonal(rig.Player, on);
        _now += global::KilnAutoLoot.Server.Smash.RetryMs;   // targets hit before wait their retry time
        Assert.NotEmpty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));
    }

    [Fact]
    public void A_joined_players_message_turning_smash_off_counts_once_the_tick_applies_it()
    {
        var rig = new LoopRig(AllOn);
        var server = new ServerSettings();
        rig.Loop.Configure(server);
        Assert.NotEmpty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));

        var off = new PersonalSettings();
        AllOn(off);
        off.Smash = false;
        rig.Clock.NowMs += LootLoop.SettingsGapMs;
        rig.Loop.QueuePersonal(rig.Player, SettingsJson.Write(off), []);
        var applied = rig.Loop.ApplyQueuedSettings();   // LootServer.OnTick, every tick
        Assert.Equal([(rig.Player, true)], applied);
        Assert.False(rig.Loop.SettingsOf(rig.Player).Smash);
        for (var i = 0; i < 5; i++) Assert.Empty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));
    }

    [Fact]
    public void The_World_tab_turning_smashing_off_counts_from_the_next_run_and_back_on_again()
    {
        var rig = new LoopRig(AllOn);
        var server = new ServerSettings();
        rig.Loop.Configure(server);
        Assert.NotEmpty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));

        // Kiln changes the config's values in place and raises Changed; LootServer.OnConfigChanged calls Configure
        server.Limits.AllowSmash = false;
        rig.Loop.Configure(server);
        Assert.False(rig.Loop.SettingsOf(rig.Player).Smash);
        for (var i = 0; i < 5; i++) Assert.Empty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));

        server.Limits.AllowSmash = true;
        rig.Loop.Configure(server);
        Assert.True(rig.Loop.SettingsOf(rig.Player).Smash);   // the player's own choice comes back
        _now += global::KilnAutoLoot.Server.Smash.RetryMs;
        Assert.NotEmpty(RunOnce(rig.Loop.SettingsOf(rig.Player), server.Limits));
    }

    [Fact]
    public void Smash_off_survives_the_trip_to_the_server_and_back()
    {
        var off = new PersonalSettings();
        AllOn(off);
        off.Smash = false;
        var read = SettingsJson.Read(SettingsJson.Write(off));
        Assert.NotNull(read);
        Assert.False(read!.Smash);
        var limits = SettingsJson.ReadLimits(SettingsJson.WriteLimits(new ServerLimits { AllowSmash = false }));
        Assert.False(limits!.AllowSmash);
    }
}
