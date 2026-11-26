// SPDX-License-Identifier: MIT
// The bag the rules see is the backpack (0.2.13). A player's Items.Read has five Generic inventories of 8 slots, two of
// which are the action bars (Ecs.SlotSelection.actionbarEntityIds; lab dev31-autoloot-slots, a 1_Player: links 2 and 3).
// Kiln's Items.Move never puts a stack into the action bar ("no room in entity's backpack" with 8 of its slots empty), so
// empty action-bar slots counted as free bag slots let keepFreeSlots fill the whole backpack and planned slot moves the
// game refuses.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public sealed class BackpackReadTests
{
    private const uint Pack0 = 10, Pack1 = 11, Bar0 = 12, Bar1 = 13, Pack2 = 14, Currency = 15;

    /// <summary>Eight slots of an inventory: the given stacks first, the rest empty.</summary>
    private static IEnumerable<InventorySlotRead> Inv(uint inventory, bool generic, params (uint Item, int Count)[] stacks) =>
        Enumerable.Range(0, 8).Select(i => i < stacks.Length
            ? new InventorySlotRead(stacks[i].Item, stacks[i].Count, generic, inventory)
            : new InventorySlotRead(0, 0, generic, inventory));

    private static List<InventorySlotRead> Player(int packUsed, int barUsed)
    {
        var slots = new List<InventorySlotRead>();
        foreach (var (inv, bar) in new[] { (Pack0, false), (Pack1, false), (Bar0, true), (Bar1, true), (Pack2, false) })
        {
            var used = bar ? Math.Min(8, barUsed) : Math.Min(8, packUsed);
            if (bar) barUsed -= used; else packUsed -= used;
            slots.AddRange(Inv(inv, true, Enumerable.Range(0, used).Select(i => ((uint)(100 + i), 1)).ToArray()));
        }
        slots.AddRange(Inv(Currency, false));
        return slots;
    }

    [Fact]
    public void Empty_action_bar_slots_are_not_free_bag_slots()
    {
        var read = BackpackRead.From(Player(packUsed: 20, barUsed: 2), Bar0, Bar1);
        Assert.Equal(4, read.FreeSlots);          // 24 backpack slots, 20 used; the 14 empty bar slots do not count
    }

    [Fact]
    public void A_stack_in_the_action_bar_is_carried_but_gives_no_room_to_top_up()
    {
        var slots = Player(packUsed: 0, barUsed: 0);
        slots[16] = new InventorySlotRead(7, 5, true, Bar0);   // flint on the first action bar
        slots[0] = new InventorySlotRead(1, 5, true, Pack0);   // twigs in the backpack
        var read = BackpackRead.From(slots, Bar0, Bar1);
        Assert.Contains(new BagSlot(7, 5, false), read.Slots);
        Assert.Contains(new BagSlot(1, 5, true), read.Slots);
        Assert.Equal(23, read.FreeSlots);
    }

    [Fact]
    public void Without_known_action_bars_every_generic_slot_counts_as_before()
    {
        var read = BackpackRead.From(Player(packUsed: 20, barUsed: 2), 0, 0);
        Assert.Equal(18, read.FreeSlots);
        Assert.All(read.Slots, s => Assert.True(s.Backpack));
    }
}
