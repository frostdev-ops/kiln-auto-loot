// SPDX-License-Identifier: MIT
// A player's bag as one scan sees it: read once at the scan's first need, then kept up to date with what the scan
// itself takes, so two targets in one scan never count the same room twice.
//
//   Room in carried stacks: per item, over the backpack slots only (the game's Generic inventory, where it tops up
//   stacks on a pickup), the item's max stack minus the stack's count. Slots elsewhere (action bar, equipment) give no
//   room: a missed top-up there costs a pickup left behind, never an overshoot.
//   Held: per item, over every slot the read returns (backpack, action bar, equipment): what carry limits count.
//   Max stack: what the world uses now (the caller decides: the game's own raised by the server's stack size); 0 =
//   not known, which gives no room.
namespace KilnAutoLoot.Core;

/// <summary>One non-empty slot of a player's inventories. <paramref name="Backpack"/>: a slot of the Generic inventory.</summary>
public readonly record struct BagSlot(uint Item, int Count, bool Backpack);

public sealed class Bag
{
    private readonly Dictionary<uint, int> _held = new();
    private readonly Dictionary<uint, int> _room = new();
    private readonly Func<uint, int> _maxStack;

    /// <param name="freeSlots">free backpack slots; below 0 = not known.</param>
    /// <param name="maxStack">the max stack the world uses now for an item; 0 or less = not known.</param>
    public Bag(IEnumerable<BagSlot> slots, int freeSlots, Func<uint, int> maxStack)
    {
        FreeSlots = freeSlots;
        _maxStack = maxStack;
        foreach (var s in slots)
        {
            if (s.Item == 0 || s.Count <= 0) continue;
            _held[s.Item] = _held.GetValueOrDefault(s.Item) + s.Count;
            if (!s.Backpack) continue;
            var max = maxStack(s.Item);
            if (max > 1 && s.Count < max) _room[s.Item] = _room.GetValueOrDefault(s.Item) + (max - s.Count);
        }
    }

    /// <summary>Free backpack slots now (after what this scan took); below 0 = not known.</summary>
    public int FreeSlots { get; private set; }

    /// <summary>Room left in the stacks of <paramref name="item"/> already carried in the backpack.</summary>
    public int Room(uint item) => _room.GetValueOrDefault(item);

    /// <summary>How many of <paramref name="item"/> the player carries.</summary>
    public int Held(uint item) => _held.GetValueOrDefault(item);

    /// <summary>How many the player carries of all these items together.</summary>
    public int HeldOf(ISet<uint> items)
    {
        long sum = 0;
        foreach (var (i, n) in _held) if (items.Contains(i)) sum += n;
        return (int)Math.Min(sum, int.MaxValue);
    }

    /// <summary>The scan sent a take of <paramref name="count"/>: it fills carried stacks first, the rest starts new stacks
    /// (each one a free slot less).</summary>
    public void Took(uint item, int count)
    {
        if (item == 0 || count <= 0) return;
        _held[item] = Held(item) + count;
        var room = Room(item);
        var into = Math.Min(room, count);
        if (into > 0)
        {
            if (room - into > 0) _room[item] = room - into;
            else _room.Remove(item);
        }
        var rest = count - into;
        if (rest <= 0 || FreeSlots < 0) return;
        var max = _maxStack(item);
        var stacks = max > 1 ? (rest + max - 1) / max : rest;
        FreeSlots = Math.Max(0, FreeSlots - stacks);
        if (max > 1 && rest % max != 0) _room[item] = Room(item) + (max - rest % max);
    }
}
