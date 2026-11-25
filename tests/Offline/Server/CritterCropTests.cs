// SPDX-License-Identifier: MIT
// 0.2.3: critters (the fishing bait swarms: fireflies, moths, grasshoppers, scarabs, pillbugs, frogs, maggots) get their
// own switch and are taken like the fixed pickups they are built like (mushrooms: a pickup with a default inventory and
// the game's pickup interaction), at once and without an accept; ripe crops have one switch for the world and one for
// a base (a player's report: base fields are decoration).
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class CritterCropTests
{
    private const uint Frog = 40;
    private static readonly string[] Swarm = [LootLoop.PickupComponent, LootLoop.OfferComponent];
    private static readonly string[] Bed = [LootLoop.OfferComponent, LootLoop.InventoryComponent];

    private static ItemTable Items() => ItemTable.Parse(new StringReader(Fixtures.ItemsText +
        "40\tMaterial_T6_Frogs_FishingBait\tFrog\tFrosch\t1\tAmmunition\tAmmunition\n"));

    private static LoopRig Rig(Action<PersonalSettings>? settings = null) => new(settings, items: Items());

    private static void NearBase(LoopRig rig, bool near) => rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { NearBase = near };

    private static bool Took(LoopRig rig, FakeEntity e) => rig.Game.Loots.Any(l => l.Host == e.Id);

    // ---------------------------------------------------------------- critters

    [Fact]
    public void A_frog_swarm_is_taken_at_once_as_a_pickup_without_an_accept()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 3, Swarm);   // its pickup names no item: the bait is its default inventory
        frogs.Offer = new Offer(true, false, 5);
        rig.ScanAfter(100);
        Assert.True(Took(rig, frogs));
        Assert.Empty(rig.Game.Interacts);
        Assert.Equal(1, rig.Loop.Counters.PickupsSeen);
        Assert.Equal(0, rig.Loop.Counters.ContainersSeen);
    }

    [Fact]
    public void Critters_follow_their_own_switch_not_plants()
    {
        Assert.True(new PersonalSettings().Critters);
        var noPlants = Rig(s => s.Plants = false);
        var a = noPlants.Game.Add(LoopRig.Frogs, 3, Swarm);
        noPlants.ScanFor(500);
        Assert.True(Took(noPlants, a));

        var off = Rig(s => s.Critters = false);
        var b = off.Game.Add(LoopRig.Frogs, 3, Swarm);
        var bush = off.Game.Add(LoopRig.Bush, 4, Bed);
        off.ScanFor(3000);
        Assert.False(Took(off, b));
        Assert.True(Took(off, bush));
        Assert.True(off.Loop.Counters.GroupOff > 0);
    }

    [Fact]
    public void A_critter_is_judged_by_its_bait_the_skip_list_and_the_only_list()
    {
        var skip = Rig(s => s.SkipList = "=Material_T6_Frogs_FishingBait");
        var a = skip.Game.Add(LoopRig.Frogs, 3, Swarm);
        skip.ScanFor(500);
        Assert.False(Took(skip, a));
        Assert.True(skip.Loop.Counters.YieldSkipped > 0);
        Assert.Contains(skip.Notices, n => n.Msg.ItemId == Frog);   // "left behind: Frog"

        var only = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Material_T6_Frogs_FishingBait"; });
        var b = only.Game.Add(LoopRig.Frogs, 3, Swarm);
        var twig = only.Game.Add(LoopRig.Twig, 4, [], pickupItem: 1);
        only.ScanFor(500);
        Assert.True(Took(only, b));
        Assert.False(Took(only, twig));
    }

    [Fact]
    public void Taking_a_critter_by_hand_names_its_bait_for_the_gather_focus()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 3, Swarm);
        rig.ScanAfter(100);
        Assert.Equal(Frog, rig.Loop.HandPickupItem(frogs.Id));
    }

    // Tyrion67 (0.2.10): a fire fly swarm whose template carries no PickupItem. The loop finds it by its offer, takes it
    // as the pickup a critter is (the loot-all hands over its default inventory, as for every swarm whose pickup names
    // no item), at once and without an accept; a hand take names its bait.
    [Fact]
    public void A_swarm_without_a_pickup_component_is_taken_at_once_as_a_pickup()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 3, [LootLoop.OfferComponent]);
        frogs.Offer = new Offer(true, false, 5);
        rig.ScanAfter(100);
        Assert.True(Took(rig, frogs));
        Assert.Empty(rig.Game.Interacts);
        Assert.Equal(1, rig.Loop.Counters.PickupsSeen);
        Assert.Equal(0, rig.Loop.Counters.ContainersSeen);
        Assert.Equal(Frog, rig.Loop.HandPickupItem(frogs.Id));
    }

    [Fact]
    public void A_critter_reaches_as_far_as_the_main_radius()
    {
        var rig = Rig(s => { s.Radius = 10; s.PlantsRadius = 2; });
        var frogs = rig.Game.Add(LoopRig.Frogs, 8, Swarm);
        rig.ScanFor(500);
        Assert.True(Took(rig, frogs));
    }

    [Fact]
    public void A_frog_swarm_seen_again_is_not_taken_twice_in_a_row()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 3, Swarm);
        rig.ScanFor(1000);
        Assert.Single(rig.Game.Loots);
    }

    // ---------------------------------------------------------------- crops in the world and in a base

    [Fact]
    public void Ripe_crops_in_a_base_follow_their_own_switch()
    {
        Assert.True(new PersonalSettings().Crops);
        Assert.True(new PersonalSettings().CropsInBase);
        var rig = Rig(s => s.CropsInBase = false);
        NearBase(rig, true);
        var bed = rig.Game.Add(LoopRig.Crop, 3, Bed);
        rig.ScanFor(5000);
        Assert.False(Took(rig, bed));
        Assert.Empty(rig.Game.Interacts);
        Assert.True(rig.Loop.Counters.GroupOff > 0);

        NearBase(rig, false);   // the same bed seen from out in the world
        rig.ScanFor(5000);
        Assert.True(Took(rig, bed));
    }

    [Fact]
    public void Ripe_crops_out_in_the_world_follow_the_crops_switch_only()
    {
        var rig = Rig(s => { s.Crops = false; s.CropsInBase = true; });
        var bed = rig.Game.Add(LoopRig.Crop, 3, Bed);
        rig.ScanFor(5000);
        Assert.False(Took(rig, bed));

        NearBase(rig, true);
        rig.ScanFor(5000);
        Assert.True(Took(rig, bed));
    }

    [Fact]
    public void Planted_algae_in_a_base_follows_crops_in_base()
    {
        // 0.2.6 (netjunk): algae planted in his base was still taken with "Ripe crops in a base" off. Planted algae
        // grows into the world's own algae templates, which were plants only.
        var rig = Rig(s => s.CropsInBase = false);
        NearBase(rig, true);
        var algae = rig.Game.Add(LoopRig.Algae, 3, Bed);
        rig.ScanFor(5000);
        Assert.False(Took(rig, algae));
        Assert.Empty(rig.Game.Interacts);

        NearBase(rig, false);   // the same algae out in the world: a plant, the Plants switch
        rig.ScanFor(5000);
        Assert.True(Took(rig, algae));
    }
}
