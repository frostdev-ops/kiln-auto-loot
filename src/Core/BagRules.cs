// SPDX-License-Identifier: MIT
// What the bag lets one take be, as pure rules:
//   - top-up only (fillCarriedStacks, or keepFreeSlots N > 0 once N or fewer backpack slots are free): an item goes
//     only into stacks the player already carries, up to full, never into a new stack;
//   - carry limits: no more of an item than its limit (a group limit counts the whole group);
//   - a bag that could not be read while a rule is on: nothing is taken (the rules cannot be kept).
namespace KilnAutoLoot.Core;

/// <summary>Why the bag stops a take.</summary>
public enum BagRefusal { None, NoRoom, CarryLimit }

public static class BagRules
{
    /// <summary>A bag rule is on: the bag has to be read for this player.</summary>
    public static bool NeedsBag(PersonalSettings s, CarryLimits limits) =>
        s.FillCarriedStacks || s.KeepFreeSlots > 0 || !limits.IsEmpty;

    /// <summary>Only stacks already carried are topped up now.</summary>
    public static bool TopUpOnly(PersonalSettings s, Bag bag) =>
        s.FillCarriedStacks || (s.KeepFreeSlots > 0 && bag.FreeSlots >= 0 && bag.FreeSlots <= s.KeepFreeSlots);

    /// <summary>How many of <paramref name="item"/> may be taken now; int.MaxValue = no bag rule limits it.</summary>
    /// <param name="bag">null = the bag could not be read.</param>
    public static int Allowance(uint item, PersonalSettings s, Bag? bag, CarryLimits limits, out BagRefusal why)
    {
        why = BagRefusal.None;
        if (!NeedsBag(s, limits)) return int.MaxValue;
        if (bag is null)
        {
            why = BagRefusal.NoRoom;
            return 0;
        }
        var room = TopUpOnly(s, bag) ? bag.Room(item) : int.MaxValue;
        var left = limits.Left(item, bag);
        var allowed = Math.Min(room, left);
        if (allowed <= 0) why = left <= room ? BagRefusal.CarryLimit : BagRefusal.NoRoom;
        return allowed;
    }

    /// <summary>A ground pickup is taken whole or not at all: its whole amount has to fit.</summary>
    public static BagRefusal Pickup(uint item, int amount, PersonalSettings s, Bag? bag, CarryLimits limits)
    {
        var allowed = Allowance(item, s, bag, limits, out var why);
        if (allowed >= Math.Max(1, amount)) return BagRefusal.None;
        if (why != BagRefusal.None) return why;
        // some room, not enough: the tighter of the two rules decides the reason
        var left = bag is null ? 0 : limits.Left(item, bag);
        return left < Math.Max(1, amount) ? BagRefusal.CarryLimit : BagRefusal.NoRoom;
    }

    /// <summary>A harvest (plant, crop, heap) gives its whole yield in amounts the server does not know beforehand: it
    /// goes ahead only when every yield item has room for at least one more. A harvest with no known yield goes ahead
    /// only when no top-up rule is on (carry limits alone cannot name an unknown item).</summary>
    public static BagRefusal Harvest(IReadOnlyList<uint> yield, PersonalSettings s, Bag? bag, CarryLimits limits)
    {
        if (!NeedsBag(s, limits)) return BagRefusal.None;
        if (bag is null) return BagRefusal.NoRoom;
        if (yield.Count == 0) return TopUpOnly(s, bag) ? BagRefusal.NoRoom : BagRefusal.None;
        foreach (var item in yield)
            if (Allowance(item, s, bag, limits, out var why) <= 0) return why;
        return BagRefusal.None;
    }

    /// <summary>What a harvest is counted as in the bag: one of each yield item (the least it gives).</summary>
    public static void Harvested(IReadOnlyList<uint> yield, Bag? bag)
    {
        if (bag is null) return;
        foreach (var item in yield) bag.Took(item, 1);
    }
}
