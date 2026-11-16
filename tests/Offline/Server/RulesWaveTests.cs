// SPDX-License-Identifier: MIT
// The loot loop with the player's own rules: the bag (top up carried stacks, keep free slots, carry limits), the only
// list, gear rarity, the height window (looting and smashing) and gather focus.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class RulesWaveTests
{
    private static readonly string[] Pickup = [];
    private static readonly string[] Inv = [LootLoop.InventoryComponent];
    private const uint Twigs = 1, Stone = 2, Fibre = 3, Apple = 6, Flint = 7, Sword = 9;

    private static LoopRig Rig(Action<PersonalSettings> settings, ItemTable? items = null, uint[]? onlyIds = null) =>
        new(s => { s.OpenDelay = 0; settings(s); }, items: items, onlyIds: onlyIds);

    private static bool Took(LoopRig rig, FakeEntity e) => rig.Game.Loots.Any(l => l.Host == e.Id);

    // ---------------------------------------------------------------- the bag

    [Fact]
    public void Fill_carried_stacks_takes_a_pickup_of_15_into_30_of_50_and_leaves_one_of_25()
    {
        var rig = Rig(s => s.FillCarriedStacks = true);
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var small = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        small.PickupAmount = 15;
        rig.ScanAfter(100);
        Assert.True(Took(rig, small));

        var big = new LoopRig(s => s.FillCarriedStacks = true);
        big.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var e = big.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        e.PickupAmount = 25;
        big.ScanFor(1000);
        Assert.False(Took(big, e));
        Assert.True(big.Loop.Counters.NoRoom > 0);
    }

    [Fact]
    public void Two_pickups_in_one_scan_share_the_room_the_bag_read_showed()
    {
        var rig = Rig(s => s.FillCarriedStacks = true);
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var a = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        var b = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Twigs);
        a.PickupAmount = b.PickupAmount = 15;
        rig.ScanAfter(100);
        Assert.True(Took(rig, a));
        Assert.False(Took(rig, b));               // 15 of the 20 are spoken for
        Assert.Equal(1, rig.Game.BagReads);       // read once per scan
    }

    [Fact]
    public void Fill_carried_stacks_moves_20_of_a_chest_slot_of_40_sticks()
    {
        var rig = Rig(s => s.FillCarriedStacks = true);
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Twigs, 40), new ContainerSlot(12, Twigs, 10), new ContainerSlot(13, Apple, 2)];
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);             // something stays: no loot-all
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
        Assert.Equal([20], rig.Game.MoveCounts.ToArray());

        rig.Game.Moves[0].Done(MoveOutcome.Done);
        rig.Game.BagSlots = [new BagSlot(Twigs, 50, true)];
        chest.Slots = [new ContainerSlot(11, Twigs, 20), new ContainerSlot(12, Twigs, 10), new ContainerSlot(13, Apple, 2)];
        rig.Game.Clear();
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Moves);             // the stack is full now
    }

    [Fact]
    public void A_move_still_in_flight_counts_in_the_next_bag_read()
    {
        var rig = Rig(s => s.FillCarriedStacks = true);
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Twigs, 40), new ContainerSlot(13, Apple, 2)];
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Moves);
        var pickup = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);   // the move has not answered
        rig.ScanAfter(100);
        Assert.False(Took(rig, pickup));
    }

    [Fact]
    public void Keep_free_slots_tops_up_only_once_that_few_slots_are_free()
    {
        var rig = Rig(s => s.KeepFreeSlots = 2);
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        rig.Game.FreeSlotCount = 3;
        var apple = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Apple);
        rig.ScanAfter(100);
        Assert.True(Took(rig, apple));            // 3 free: anything goes

        var full = Rig(s => s.KeepFreeSlots = 2);
        full.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        full.Game.FreeSlotCount = 2;
        var apple2 = full.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Apple);
        var twig = full.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Twigs);
        full.ScanAfter(100);
        Assert.False(Took(full, apple2));         // would start a new stack
        Assert.True(Took(full, twig));            // tops up the carried one
    }

    [Fact]
    public void A_carry_limit_by_item_stops_at_the_limit_within_one_scan()
    {
        var rig = Rig(s => s.CarryLimits = "=Twigs:40");
        rig.Game.BagSlots = [new BagSlot(Twigs, 30, true)];
        var a = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        var b = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Twigs);
        a.PickupAmount = 10;
        rig.ScanAfter(100);
        Assert.True(Took(rig, a));
        Assert.False(Took(rig, b));
        Assert.True(rig.Loop.Counters.CarryLimited > 0);
    }

    [Fact]
    public void A_group_carry_limit_counts_the_whole_group_and_limits_container_moves()
    {
        var rig = Rig(s => s.CarryLimits = "#Materials:35");
        rig.Game.BagSlots = [new BagSlot(Stone, 30, true)];
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Twigs, 8), new ContainerSlot(12, Apple, 3)];
        rig.ScanAfter(100);
        Assert.Equal([11ul, 12ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
        Assert.Equal([5, 0], rig.Game.MoveCounts.ToArray());   // 5 of 8 twigs, the apples whole (not in the group)
    }

    [Fact]
    public void Slots_of_one_container_share_a_group_limit()
    {
        var rig = Rig(s => s.CarryLimits = "#Materials:35");
        rig.Game.BagSlots = [new BagSlot(Stone, 30, true)];
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Twigs, 8), new ContainerSlot(12, Stone, 8)];
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        var moved = rig.Game.Moves.Select((m, i) => rig.Game.MoveCounts[i] == 0 ? 8 : rig.Game.MoveCounts[i]).Sum();
        Assert.Equal(5, moved);
    }

    [Fact]
    public void Keep_free_slots_stops_a_container_before_it_crosses_into_the_kept_slots()
    {
        var rig = Rig(s => s.KeepFreeSlots = 2);
        rig.Game.BagSlots = [];
        rig.Game.FreeSlotCount = 3;                // one usable slot above the two kept
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Apple, 2), new ContainerSlot(12, Flint, 3)];
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    [Fact]
    public void Keep_free_slots_leaves_a_pickup_that_needs_more_than_the_usable_slots()
    {
        var rig = Rig(s => s.KeepFreeSlots = 2);
        rig.Game.BagSlots = [];
        rig.Game.FreeSlotCount = 3;
        var big = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        big.PickupAmount = 120;                    // three stacks of 50, one slot usable
        var fits = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Stone);
        fits.PickupAmount = 50;
        rig.ScanAfter(100);
        Assert.False(Took(rig, big));
        Assert.True(Took(rig, fits));
    }

    [Fact]
    public void Under_a_bag_rule_a_bush_is_harvested_only_when_its_live_contents_fit_whole()
    {
        var rig = Rig(s => s.FillCarriedStacks = true, items: WithFibreStack());
        rig.Game.BagSlots = [new BagSlot(Fibre, 40, true)];
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = [new ContainerSlot(1, Fibre, 10)];
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));             // 10 into the 40 of 50

        var over = Rig(s => s.FillCarriedStacks = true, items: WithFibreStack());
        over.Game.BagSlots = [new BagSlot(Fibre, 45, true)];
        var bush2 = over.Game.Add(LoopRig.Bush, 2, Inv);
        bush2.Slots = [new ContainerSlot(1, Fibre, 10)];
        over.ScanFor(500);
        Assert.False(Took(over, bush2));          // 10 do not fit into 5

        var unread = Rig(s => s.FillCarriedStacks = true, items: WithFibreStack());
        unread.Game.BagSlots = [new BagSlot(Fibre, 10, true)];
        var bush3 = unread.Game.Add(LoopRig.Bush, 2, Inv);   // no readable contents
        unread.ScanFor(500);
        Assert.False(Took(unread, bush3));
        Assert.True(unread.Loop.Counters.HarvestUnread > 0);
    }

    [Fact]
    public void A_harvest_reserves_its_whole_contents_for_the_rest_of_the_scan()
    {
        var rig = Rig(s => s.CarryLimits = "=Plant Fiber:12", items: WithFibreStack());
        rig.Game.BagSlots = [];
        var a = rig.Game.Add(LoopRig.Bush, 2, Inv);
        a.Slots = [new ContainerSlot(1, Fibre, 8)];
        var b = rig.Game.Add(LoopRig.Bush, 3, Inv);
        b.Slots = [new ContainerSlot(1, Fibre, 8)];
        rig.ScanAfter(100);
        Assert.True(Took(rig, a));
        Assert.False(Took(rig, b));               // 8 + 8 > 12
    }

    [Fact]
    public void Without_a_bag_rule_a_harvest_is_not_read()
    {
        var rig = Rig(s => s.CarryLimits = "=Apple:1");   // names nothing the bush gives
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));
    }

    private static ItemTable WithFibreStack() =>
        ItemTable.FromRows(Fixtures.Items().All.Select(r => r.Id == Fibre ? r with { OriginalMaxStack = 50 } : r));

    [Fact]
    public void The_servers_stack_size_raises_the_room()
    {
        var rig = Rig(s => s.FillCarriedStacks = true);
        rig.Game.StackWish = 9999;
        rig.Game.BagSlots = [new BagSlot(Twigs, 50, true)];
        var e = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        e.PickupAmount = 500;
        rig.ScanAfter(100);
        Assert.True(Took(rig, e));                // 50 of 9999
    }

    // ---------------------------------------------------------------- only list, rarity

    [Fact]
    public void Only_list_takes_a_twig_bush_and_only_the_listed_slots()
    {
        var rig = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Twigs"; });
        var heap = rig.Game.Add(LoopRig.Twig, 2, Inv);   // a twig bush: ground, no pickup, yield twigs
        heap.Slots = [new ContainerSlot(11, Twigs, 5), new ContainerSlot(12, Stone, 3)];
        var bush = rig.Game.Add(LoopRig.Bush, 3, Inv);   // gives plant fibre only
        var stone = rig.Game.Add(LoopRig.Twig, 4, Pickup, pickupItem: Stone);
        var twig = rig.Game.Add(LoopRig.Twig, 5, Pickup, pickupItem: Twigs);
        rig.ScanFor(500);
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).Distinct().ToArray());
        Assert.False(Took(rig, bush));
        Assert.False(Took(rig, stone));
        Assert.True(Took(rig, twig));
        Assert.True(rig.Loop.Counters.NotListed > 0);
        Assert.Empty(rig.Notices);                // no "left behind" for unlisted items
    }

    [Fact]
    public void Only_list_harvests_a_plant_when_any_of_its_yield_is_listed()
    {
        var rig = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "fiber"; });
        var bush = rig.Game.Add(LoopRig.Bush, 3, Inv);
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));
    }

    [Fact]
    public void Only_ids_from_the_players_game_match_on_a_server_with_internal_names()
    {
        var rig = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Äste"; }, items: Fixtures.ServerItems(), onlyIds: [Twigs]);
        var twig = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Twigs);
        rig.ScanAfter(100);
        Assert.True(Took(rig, twig));
    }

    [Fact]
    public void Gear_below_the_lowest_rarity_stays_on_the_ground()
    {
        var rig = Rig(s => s.MinGearRarity = GearRarity.Rare, items: BagRulesTests.GearItems());
        var uncommon = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Sword);
        uncommon.Rarity = 1;
        var rare = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Sword);
        rare.Rarity = 2;
        rig.ScanAfter(100);
        Assert.False(Took(rig, uncommon));
        Assert.True(Took(rig, rare));
        Assert.True(rig.Loop.Counters.RarityLow > 0);
    }

    [Fact]
    public void Gear_below_the_lowest_rarity_stays_in_a_chest()
    {
        var rig = Rig(s => s.MinGearRarity = GearRarity.Uncommon, items: BagRulesTests.GearItems());
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Sword, 1, 0), new ContainerSlot(12, Sword, 1, 3), new ContainerSlot(13, Stone, 5)];
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal([12ul, 13ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    // ---------------------------------------------------------------- height window

    [Theory]
    [InlineData(3.0, true)]
    [InlineData(5.0, false)]
    [InlineData(-5.0, false)]
    [InlineData(-3.5, true)]
    public void The_height_window_holds_looting_both_ways(double y, bool taken)
    {
        var rig = Rig(s => { s.HeightAbove = 4; s.HeightBelow = 4; });
        var twig = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Twigs, y: y);
        rig.ScanAfter(100);
        Assert.Equal(taken, Took(rig, twig));
    }

    [Fact]
    public void A_height_of_0_is_no_limit_on_that_side()
    {
        var rig = Rig(s => { s.HeightAbove = 0; s.HeightBelow = 4; });
        var high = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Twigs, y: 10);
        var low = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Twigs, y: -10);
        rig.ScanAfter(100);
        Assert.True(Took(rig, high));
        Assert.False(Took(rig, low));
        Assert.True(rig.Loop.Counters.HeightOut > 0);
    }

    private sealed class SmashWorld : ISmashWorld
    {
        public readonly List<SmashTarget> Targets = [];
        public readonly List<EntityId> Hits = [];
        public IReadOnlyList<EntityId> Candidates() => Targets.Select(t => t.Entity).ToList();
        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> c, WorldPosition at, double radius) =>
            c.Where(e => Inspect(e) is { } t && t.Position.DistanceTo(at) <= radius).ToList();
        public SmashTarget? Inspect(EntityId e) => Targets.Where(t => t.Entity == e).Select(t => (SmashTarget?)t).FirstOrDefault();
        public bool InSaveZone(EntityId player) => false;
        public bool NearBuildZone(EntityId player) => false;
        public void Hit(EntityId player, in SmashTarget target, SmashKind kind) => Hits.Add(target.Entity);
    }

    [Fact]
    public void Smashing_keeps_to_the_height_window_too()
    {
        var crate = new Guid("00000000-0000-0000-0000-00000000b001");
        var world = new SmashWorld();
        world.Targets.Add(new SmashTarget(new EntityId(201), crate, 1, new WorldPosition(1, 3, 0)));
        world.Targets.Add(new SmashTarget(new EntityId(202), crate, 1, new WorldPosition(1, 6, 0)));
        world.Targets.Add(new SmashTarget(new EntityId(203), crate, 1, new WorldPosition(1, -6, 0)));
        var smash = new global::KilnAutoLoot.Server.Smash(null!, world, () => 1_000_000,
            () => SmashTable.Parse(new StringReader($"# t\n{crate}\tsmash\tCrate\n")));
        smash.Run(new EntityId(1), new WorldPosition(0, 0, 0), new PersonalSettings { HeightAbove = 4, HeightBelow = 4 }, new ServerLimits());
        Assert.Equal([new EntityId(201)], world.Hits.ToArray());
    }

    // ---------------------------------------------------------------- gather focus

    [Fact]
    public void Focus_from_a_hand_take_toggles_on_and_off_and_clears()
    {
        var rig = Rig(_ => { });
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);   // nothing taken by hand yet

        var hand = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Twigs);
        Assert.Equal(Twigs, rig.Loop.HandPickupItem(hand.Id));
        rig.Loop.TookByHand(rig.Player, Twigs);
        rig.Game.Remove(hand);
        Assert.Equal(Twigs, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
        Assert.Equal(Twigs, rig.Loop.FocusOf(rig.Player));

        var stone = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: Stone);
        var twig = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: Twigs);
        rig.ScanAfter(100);
        Assert.False(Took(rig, stone));
        Assert.True(Took(rig, twig));
        Assert.True(rig.Loop.Counters.NotFocus > 0);

        rig.Clock.NowMs += 1000;                  // a new second of the command budget
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);   // again: off
        Assert.Equal(Twigs, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Focus);
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Focus);
    }

    [Fact]
    public void Focus_stays_through_new_settings_and_ends_when_the_player_leaves_or_the_world_stops()
    {
        var rig = Rig(_ => { });
        rig.Loop.TookByHand(rig.Player, Twigs);
        rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { Radius = 10 });
        Assert.Equal(Twigs, rig.Loop.FocusOf(rig.Player));
        rig.Loop.Leave(rig.Player);
        Assert.Equal(0u, rig.Loop.FocusOf(rig.Player));
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);   // the hand item went too

        rig.Loop.TookByHand(rig.Player, Twigs);
        rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);
        rig.Loop.Reset();
        Assert.Equal(0u, rig.Loop.FocusOf(rig.Player));
    }

    [Fact]
    public void Focus_on_a_container_moves_only_the_focus_item_and_beats_the_skip_list()
    {
        var rig = Rig(s => s.SkipList = "=Twigs");
        rig.Loop.TookByHand(rig.Player, Twigs);
        rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);
        var chest = rig.Game.Add(LoopRig.Chest, 4, Inv);
        chest.Slots = [new ContainerSlot(11, Twigs, 5), new ContainerSlot(12, Stone, 3)];
        rig.ScanAfter(100);
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    [Fact]
    public void The_hand_item_comes_from_an_item_that_actually_arrived()
    {
        var rig = Rig(_ => { });
        var twig = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Twigs);
        rig.Loop.HandLoot(rig.Player, twig.Id, lootAll: false);
        rig.Loop.ItemReceived(rig.Player, Twigs);              // the twigs arrived
        Assert.Equal(Twigs, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
        rig.Loop.Command(rig.Player, CommandKind.ClearFocus);

        var sword = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Sword);
        rig.Loop.HandLoot(rig.Player, sword.Id, lootAll: false);   // refused: a full bag, a zone; nothing arrives
        rig.Clock.NowMs += LootLoop.HandTakeWindowMs + 1;
        rig.Loop.ItemReceived(rig.Player, Sword);              // too late to belong to that take
        Assert.Equal(Twigs, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
    }

    [Fact]
    public void Our_own_takes_loot_alls_and_other_players_items_never_set_the_hand_item()
    {
        var rig = Rig(_ => { });
        var stone = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: Stone);
        rig.Loop.ItemReceived(rig.Player, Stone);              // the mod's own take: nothing pending
        rig.Loop.HandLoot(rig.Player, stone.Id, lootAll: true);   // a loot-all by hand: not a single pickup
        rig.Loop.ItemReceived(rig.Player, Stone);
        var other = rig.Game.AddPlayer(2, 0, 0);
        rig.Loop.Join(other);
        rig.Loop.HandLoot(rig.Player, stone.Id, lootAll: false);
        rig.Loop.ItemReceived(other, Stone);                   // another player's inventory
        rig.Loop.ItemReceived(rig.Player, Apple);              // a different item
        Assert.Equal(0u, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
        rig.Loop.ItemReceived(rig.Player, Stone);              // now it arrives
        Assert.Equal(Stone, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);
    }

    [Fact]
    public void Commands_are_budgeted_per_player_and_redundant_answers_coalesced()
    {
        var rig = Rig(_ => { });
        rig.Loop.TookByHand(rig.Player, Twigs);
        Assert.True(rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Reply);    // nothing changed: answered once
        Assert.False(rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Reply);   // and not again
        var on = rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);
        Assert.Equal((Twigs, true), (on.Focus, on.Reply));                           // a change: answered
        Assert.True(rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Reply);
        var dropped = rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);         // the fifth in this second
        Assert.Equal((0u, false), (dropped.Focus, dropped.Reply));
        Assert.Equal(1, rig.Loop.Counters.CommandsDropped);

        rig.Clock.NowMs += 1000;
        Assert.Equal(Twigs, rig.Loop.Command(rig.Player, CommandKind.ToggleFocus).Focus);

        rig.Loop.Leave(rig.Player);                            // the budget and the answers go with the player
        Assert.True(rig.Loop.Command(rig.Player, CommandKind.ClearFocus).Reply);
    }

    [Fact]
    public void A_world_with_the_mod_off_answers_commands_without_changing_focus()
    {
        var rig = Rig(_ => { });
        rig.Loop.TookByHand(rig.Player, Twigs);
        var r = rig.Loop.Command(rig.Player, CommandKind.ToggleFocus, apply: false);
        Assert.Equal((0u, true), (r.Focus, r.Reply));
    }

    [Fact]
    public void The_effective_json_is_written_once_per_change()
    {
        var rig = Rig(s => s.Radius = 12);
        var a = rig.Loop.EffectiveJsonOf(rig.Player);
        Assert.Same(a, rig.Loop.EffectiveJsonOf(rig.Player));
        Assert.Equal(12f, SettingsJson.Read(a)!.Radius);
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { Radius = 9 });
        Assert.Equal(9f, SettingsJson.Read(rig.Loop.EffectiveJsonOf(rig.Player))!.Radius);
    }

    [Fact]
    public void Line_of_sight_is_reported_as_the_game_layer_can_check_it()
    {
        var rig = Rig(_ => { });
        Assert.True(rig.Loop.SightAvailable);
        rig.Game.SightOn = false;
        Assert.False(rig.Loop.SightAvailable);
    }
}
