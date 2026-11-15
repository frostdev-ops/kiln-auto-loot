// SPDX-License-Identifier: MIT
// What the bag lets one take be, as pure rules:
//   - fillCarriedStacks: an item goes only into stacks the player already carries, up to full, never a new stack;
//   - keepFreeSlots N > 0: an item goes into carried stacks, plus as many new stacks as there are free backpack slots
//     above the N kept (none once N or fewer are free);
//   - carry limits: no more of an item than its limit (a group limit counts the whole group);
//   - a bag that could not be read while a rule is on: nothing is taken (the rules cannot be kept).
// A take of several items (a container, a harvest) is planned on a copy of the bag, each part reserved at once, so the
// later parts see what the earlier ones use: free slots, shared group limits.
namespace KilnAutoLoot.Core;

/// <summary>Why the bag stops a take.</summary>
public enum BagRefusal { None, NoRoom, CarryLimit }

public static class BagRules
{
    /// <summary>A bag rule is on: the bag has to be read for this player.</summary>
    public static bool NeedsBag(PersonalSettings s, CarryLimits limits) =>
        s.FillCarriedStacks || s.KeepFreeSlots > 0 || !limits.IsEmpty;

    /// <summary>A bag rule applies to a take of these items: fillCarriedStacks, keepFreeSlots, or a carry limit naming
    /// one of them (alone or through its group).</summary>
    public static bool Applies(IEnumerable<uint> items, PersonalSettings s, CarryLimits limits)
    {
        if (s.FillCarriedStacks || s.KeepFreeSlots > 0) return true;
        foreach (var i in items) if (limits.Names(i)) return true;
        return false;
    }

    /// <summary>Only stacks already carried are topped up now: no new stack may start.</summary>
    public static bool TopUpOnly(PersonalSettings s, Bag bag) =>
        s.FillCarriedStacks || (s.KeepFreeSlots > 0 && UsableSlots(s, bag) == 0);

    /// <summary>Free backpack slots a take may fill: with keepFreeSlots those above the kept ones (0 when the free
    /// slots are not known); without it every free slot.</summary>
    public static int UsableSlots(PersonalSettings s, Bag bag) =>
        s.KeepFreeSlots <= 0 ? int.MaxValue : bag.FreeSlots < 0 ? 0 : Math.Max(0, bag.FreeSlots - s.KeepFreeSlots);

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
        long room = int.MaxValue;
        if (s.FillCarriedStacks) room = bag.Room(item);
        else if (s.KeepFreeSlots > 0) room = bag.Room(item) + bag.SlotCapacity(item, UsableSlots(s, bag));
        var left = limits.Left(item, bag);
        var allowed = (int)Math.Min(Math.Min(room, int.MaxValue), left);
        if (allowed <= 0) why = left <= room ? BagRefusal.CarryLimit : BagRefusal.NoRoom;
        return allowed;
    }

    /// <summary>A take that goes whole or not at all (a ground pickup, a harvest): the whole amount of every item has to
    /// fit, the parts planned one after the other on a copy of the bag.</summary>
    public static BagRefusal Fits(IReadOnlyList<(uint Item, int Count)> take, PersonalSettings s, Bag? bag, CarryLimits limits)
    {
        if (!NeedsBag(s, limits)) return BagRefusal.None;
        if (bag is null) return BagRefusal.NoRoom;
        var plan = bag.Clone();
        foreach (var (item, count) in take)
        {
            var need = Math.Max(1, count);
            var allowed = Allowance(item, s, plan, limits, out var why);
            if (allowed < need)
            {
                if (why != BagRefusal.None) return why;
                return limits.Left(item, plan) < need ? BagRefusal.CarryLimit : BagRefusal.NoRoom;
            }
            plan.Took(item, need);
        }
        return BagRefusal.None;
    }

    /// <summary>A ground pickup is taken whole or not at all: its whole amount has to fit.</summary>
    public static BagRefusal Pickup(uint item, int amount, PersonalSettings s, Bag? bag, CarryLimits limits) =>
        Fits([(item, amount)], s, bag, limits);
}
