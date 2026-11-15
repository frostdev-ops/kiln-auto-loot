// SPDX-License-Identifier: MIT
// The rules of the rules wave as pure functions: the bag (room in carried stacks, free slots, carry limits), the item
// filter (skip, only, focus), gear rarity, the height window, and the settings bounds.
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class BagRulesTests
{
    private const uint Twigs = 1, Stone = 2, Apple = 6, Flint = 7;

    /// <summary>The fixture's stacks: twigs and stone 50, everything else 20.</summary>
    private static int Max(uint item) => item is Twigs or Stone ? 50 : 20;

    private static Bag BagOf(int free, params BagSlot[] slots) => new(slots, free, Max);

    private static PersonalSettings S(Action<PersonalSettings>? change = null)
    {
        var s = new PersonalSettings();
        change?.Invoke(s);
        return s;
    }

    [Fact]
    public void Room_is_what_carried_backpack_stacks_still_hold()
    {
        var bag = BagOf(5, new BagSlot(Twigs, 30, true), new BagSlot(Twigs, 50, true), new BagSlot(Twigs, 10, false));
        Assert.Equal(20, bag.Room(Twigs));        // 30/50 + a full stack; the action-bar stack gives no room
        Assert.Equal(90, bag.Held(Twigs));        // carry limits count every slot
        Assert.Equal(0, bag.Room(Apple));
    }

    [Fact]
    public void A_stick_stack_of_30_of_50_takes_a_pickup_of_15_and_leaves_one_of_25()
    {
        var s = S(x => x.FillCarriedStacks = true);
        var bag = BagOf(5, new BagSlot(Twigs, 30, true));
        Assert.Equal(BagRefusal.None, BagRules.Pickup(Twigs, 15, s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Pickup(Twigs, 25, s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Pickup(Apple, 1, s, bag, CarryLimits.None));   // never a new stack
    }

    [Fact]
    public void What_a_scan_took_counts_against_the_room_and_new_stacks_cost_free_slots()
    {
        var bag = BagOf(3, new BagSlot(Twigs, 30, true));
        bag.Took(Twigs, 15);
        Assert.Equal(5, bag.Room(Twigs));
        Assert.Equal(3, bag.FreeSlots);
        bag.Took(Twigs, 60);                      // 5 into the stack, 55 into two new stacks of 50
        Assert.Equal(1, bag.FreeSlots);
        Assert.Equal(45, bag.Room(Twigs));        // the second new stack holds 5 of 50
        Assert.Equal(105, bag.Held(Twigs));
    }

    [Fact]
    public void An_item_whose_max_stack_is_not_known_or_1_has_no_room()
    {
        var bag = new Bag([new BagSlot(Twigs, 30, true), new BagSlot(Apple, 1, true)], 5, i => i == Twigs ? 0 : 1);
        Assert.Equal(0, bag.Room(Twigs));
        Assert.Equal(0, bag.Room(Apple));
    }

    [Fact]
    public void A_container_slot_of_40_sticks_into_a_30_of_50_stack_allows_20()
    {
        var s = S(x => x.FillCarriedStacks = true);
        var bag = BagOf(5, new BagSlot(Twigs, 30, true));
        Assert.Equal(20, BagRules.Allowance(Twigs, s, bag, CarryLimits.None, out var why));
        Assert.Equal(BagRefusal.None, why);
    }

    [Fact]
    public void Keep_free_slots_switches_to_top_up_only_once_that_few_are_free()
    {
        var s = S(x => x.KeepFreeSlots = 2);
        var three = BagOf(3, new BagSlot(Twigs, 30, true));
        Assert.Equal(20, BagRules.Allowance(Apple, s, three, CarryLimits.None, out _));   // one usable slot of 20
        Assert.Equal(70, BagRules.Allowance(Twigs, s, three, CarryLimits.None, out _));   // 20 room + one slot of 50
        Assert.False(BagRules.TopUpOnly(s, three));
        var two = BagOf(2, new BagSlot(Twigs, 30, true));
        Assert.True(BagRules.TopUpOnly(s, two));
        Assert.Equal(0, BagRules.Allowance(Apple, s, two, CarryLimits.None, out var why));
        Assert.Equal(BagRefusal.NoRoom, why);
        Assert.Equal(20, BagRules.Allowance(Twigs, s, two, CarryLimits.None, out _));

        three.Took(Apple, 5);                     // the scan's own take of a new stack brings it to 2 free
        Assert.True(BagRules.TopUpOnly(s, three));
    }

    [Fact]
    public void Carry_limits_by_item_stop_at_the_limit()
    {
        var limits = new CarryLimits("=Twigs:100", Fixtures.Items());
        var s = S(x => x.CarryLimits = "=Twigs:100");
        var bag = BagOf(10, new BagSlot(Twigs, 50, true), new BagSlot(Twigs, 30, true));
        Assert.Equal(20, BagRules.Allowance(Twigs, s, bag, limits, out _));
        Assert.Equal(BagRefusal.None, BagRules.Pickup(Twigs, 20, s, bag, limits));
        Assert.Equal(BagRefusal.CarryLimit, BagRules.Pickup(Twigs, 21, s, bag, limits));
        bag.Took(Twigs, 20);
        Assert.Equal(0, BagRules.Allowance(Twigs, s, bag, limits, out var why));
        Assert.Equal(BagRefusal.CarryLimit, why);
        Assert.Equal(int.MaxValue, BagRules.Allowance(Stone, s, bag, limits, out _));   // not named
    }

    [Fact]
    public void A_group_limit_counts_every_item_of_the_group()
    {
        var limits = new CarryLimits("#materials:100", Fixtures.Items());   // case as the skip list: any case
        var s = S(x => x.CarryLimits = "#materials:100");
        var bag = BagOf(10, new BagSlot(Twigs, 60, true), new BagSlot(Stone, 30, true));
        Assert.Equal(10, BagRules.Allowance(Stone, s, bag, limits, out _));
        Assert.Equal(10, BagRules.Allowance(Twigs, s, bag, limits, out _));
        Assert.Equal(int.MaxValue, BagRules.Allowance(Apple, s, bag, limits, out _));   // Food: not in the group
    }

    [Fact]
    public void Carry_limits_ignore_entries_without_a_number_and_take_the_tightest()
    {
        var limits = new CarryLimits("=Twigs, =Stone:abc, =Twigs:500, #Materials:80", Fixtures.Items());
        var bag = BagOf(10, new BagSlot(Twigs, 50, true));
        Assert.Equal(30, limits.Left(Twigs, bag));
        Assert.True(limits.Names(Stone));         // through the group
        Assert.False(new CarryLimits("=Twigs, x:", Fixtures.Items()).Names(Twigs));
    }

    [Fact]
    public void A_whole_take_has_to_fit_with_every_part_planned_after_the_one_before()
    {
        var s = S(x => x.FillCarriedStacks = true);
        var bag = BagOf(5, new BagSlot(Twigs, 30, true));
        Assert.Equal(BagRefusal.None, BagRules.Fits([(Twigs, 20)], s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Fits([(Twigs, 15), (Twigs, 6)], s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Fits([(Twigs, 3), (Flint, 1)], s, bag, CarryLimits.None));
        Assert.Equal(20, bag.Room(Twigs));        // planning never touches the bag itself
        Assert.Equal(BagRefusal.None, BagRules.Fits([(Flint, 9)], S(), null, CarryLimits.None));   // no rule on

        var limits = new CarryLimits("#Materials:35", Fixtures.Items());
        var held = BagOf(5, new BagSlot(Stone, 30, true));
        var ls = S(x => x.CarryLimits = "#Materials:35");
        Assert.Equal(BagRefusal.None, BagRules.Fits([(Twigs, 2), (Stone, 3)], ls, held, limits));
        Assert.Equal(BagRefusal.CarryLimit, BagRules.Fits([(Twigs, 3), (Stone, 3)], ls, held, limits));   // 6 > 5 shared
    }

    [Fact]
    public void Keep_free_slots_bounds_a_whole_take_by_the_usable_slots()
    {
        var s = S(x => x.KeepFreeSlots = 2);
        var bag = BagOf(4);                       // two usable slots
        Assert.Equal(BagRefusal.None, BagRules.Fits([(Twigs, 100)], s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Fits([(Twigs, 101)], s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Fits([(Twigs, 60), (Apple, 1)], s, bag, CarryLimits.None));
        Assert.Equal(BagRefusal.NoRoom, BagRules.Fits([(Twigs, 1)], s, BagOf(-1), CarryLimits.None));   // free slots unknown
    }

    [Fact]
    public void An_unreadable_bag_takes_nothing_while_a_bag_rule_is_on()
    {
        var s = S(x => x.KeepFreeSlots = 1);
        Assert.Equal(0, BagRules.Allowance(Twigs, s, null, CarryLimits.None, out var why));
        Assert.Equal(BagRefusal.NoRoom, why);
        Assert.Equal(int.MaxValue, BagRules.Allowance(Twigs, S(), null, CarryLimits.None, out _));
    }

    // ---------------------------------------------------------------- item filter, rarity, height

    [Fact]
    public void The_item_filter_skip_only_and_focus()
    {
        var items = Fixtures.Items();
        var skip = new IgnoreList("=Apple", items);
        var only = new IgnoreList("=Twigs, #Terrain", items);
        var skipMode = new ItemFilter(FilterMode.Skip, skip, only, 0);
        Assert.Equal(ItemRefusal.Skipped, skipMode.Check(Apple));
        Assert.Equal(ItemRefusal.None, skipMode.Check(Stone));
        var onlyMode = new ItemFilter(FilterMode.Only, skip, only, 0);
        Assert.Equal(ItemRefusal.None, onlyMode.Check(Twigs));
        Assert.Equal(ItemRefusal.None, onlyMode.Check(Flint));
        Assert.Equal(ItemRefusal.NotListed, onlyMode.Check(Stone));
        var focus = new ItemFilter(FilterMode.Skip, new IgnoreList("=Twigs", items), only, Twigs);
        Assert.Equal(ItemRefusal.None, focus.Check(Twigs));   // focus wins over the skip list
        Assert.Equal(ItemRefusal.NotFocus, focus.Check(Flint));
    }

    [Fact]
    public void Gear_below_the_lowest_rarity_stays_other_items_never()
    {
        var items = GearItems();
        Assert.True(GearRules.BelowMin(9, 1, GearRarity.Rare, items));    // an uncommon sword
        Assert.False(GearRules.BelowMin(9, 2, GearRarity.Rare, items));   // a rare one
        Assert.True(GearRules.BelowMin(10, 0, GearRarity.Uncommon, items)); // a common ring (Equipment)
        Assert.False(GearRules.BelowMin(9, 0, GearRarity.Common, items));
        Assert.False(GearRules.BelowMin(Twigs, 0, GearRarity.Legendary, items));   // not gear
        Assert.False(GearRules.BelowMin(9, -1, GearRarity.Legendary, items));      // rarity not known
        Assert.Equal(1, (int)GearRarity.Uncommon);   // Kiln's ItemRarity: Common 0, Uncommon 1, Rare 2, Epic 3, Legendary 4
        Assert.Equal(4, (int)GearRarity.Legendary);
    }

    internal static ItemTable GearItems() => ItemTable.Parse(new StringReader(Fixtures.ItemsText +
        "9\tWeapon_Sword_T1\tSword\tSchwert\t1\tWeapons\tWeapons\t\t1\n" +
        "10\tRing_T1\tRing\tRing\t1\tEquipment\tEquipment\t\t1\n"));

    [Theory]
    [InlineData(4.0, 4f, 4f, true)]
    [InlineData(4.1, 4f, 4f, false)]
    [InlineData(-4.0, 4f, 4f, true)]
    [InlineData(-4.1, 4f, 4f, false)]
    [InlineData(40.0, 0f, 4f, true)]     // 0 = no limit above
    [InlineData(-40.0, 4f, 0f, true)]    // 0 = no limit below
    public void The_height_window_both_ways(double dy, float above, float below, bool allowed) =>
        Assert.Equal(allowed, HeightWindow.Allows(dy, above, below));

    // ---------------------------------------------------------------- bounds

    [Fact]
    public void Clamp_bounds_the_new_settings()
    {
        var c = SettingsRules.Clamp(new PersonalSettings
        {
            HeightAbove = 80, HeightBelow = float.NaN, KeepFreeSlots = 200, OnlyList = null!, CarryLimits = null!,
            ItemFilter = (FilterMode)9, MinGearRarity = (GearRarity)7,
        }, new ServerLimits());
        Assert.Equal(50f, c.HeightAbove);
        Assert.Equal(0f, c.HeightBelow);
        Assert.Equal(64, c.KeepFreeSlots);
        Assert.Equal("", c.OnlyList);
        Assert.Equal("", c.CarryLimits);
        Assert.Equal(FilterMode.Skip, c.ItemFilter);
        Assert.Equal(GearRarity.Common, c.MinGearRarity);
        Assert.Equal(0, SettingsRules.Clamp(new PersonalSettings { KeepFreeSlots = -3 }, new ServerLimits()).KeepFreeSlots);
    }

    [Fact]
    public void The_only_list_and_carry_limits_are_bounded_like_the_skip_list()
    {
        var tooMany = string.Join(",", Enumerable.Range(0, SkipListBounds.MaxEntries + 1).Select(i => $"=Item{i}"));
        Assert.True(SettingsRules.ListsWithin(new PersonalSettings()));
        Assert.False(SettingsRules.ListsWithin(new PersonalSettings { OnlyList = tooMany }));
        Assert.False(SettingsRules.ListsWithin(new PersonalSettings { CarryLimits = tooMany }));
        Assert.False(SettingsRules.ListsWithin(new PersonalSettings { SkipList = tooMany }));
    }
}
