// SPDX-License-Identifier: MIT
// A player's inventory read as the bag rules see it: the backpack (0.2.13). Items.Read of a player has five Generic
// inventories of 8 slots, two of which are the action bars (Ecs.SlotSelection.actionbarEntityIds; lab, a 1_Player:
// links 2 and 3). Kiln's Items.Move never puts a stack into an action bar ("no room in entity's backpack" while 8 of its
// slots are empty, and also when a stack of the item is on the bar), and the action bar is not what a player calls their
// bag, so:
//   - free bag slots: the empty slots of the Generic inventories that are not an action bar;
//   - a stack in an action bar is carried (carry limits count it); its room serves only the game's own pickup and
//     loot-all, which top it up (Bag.BarRoom; lab dev31-review-autoloot-0213), never a slot move.
// When the action bars are not known (0), every Generic inventory counts, as before 0.2.13.
namespace KilnAutoLoot.Core;

/// <summary>One slot of a player's inventories as Items.Read gives it: the item (0 = empty), the count, whether its
/// inventory is a Generic one, and that inventory's entity id.</summary>
public readonly record struct InventorySlotRead(uint Item, int Count, bool Generic, uint Inventory);

public static class BackpackRead
{
    /// <summary>The non-empty slots (Backpack: a Generic inventory that is not an action bar) and the free backpack
    /// slots. <paramref name="bar0"/>, <paramref name="bar1"/>: the action bars' inventory ids, 0 = not known.</summary>
    public static (List<BagSlot> Slots, int FreeSlots) From(IEnumerable<InventorySlotRead> slots, uint bar0, uint bar1)
    {
        var list = new List<BagSlot>();
        var free = 0;
        foreach (var s in slots)
        {
            var backpack = s.Generic && (s.Inventory == 0 || (s.Inventory != bar0 && s.Inventory != bar1));
            if (s.Item == 0 || s.Count <= 0)
            {
                if (backpack) free++;
                continue;
            }
            list.Add(new BagSlot(s.Item, s.Count, backpack, s.Generic && !backpack));
        }
        return (list, free);
    }
}
