// SPDX-License-Identifier: MIT
// A player's bag as one scan sees it: read once at the scan's first need, then kept up to date with what the scan
// itself takes, so two targets in one scan never count the same room twice.
//
//   Room in carried stacks: per item, over the backpack slots (the Generic inventories but the two action bars,
//   Core/BackpackRead), the item's max stack minus the stack's count. Stacks on the action bar have room of their own
//   (BarRoom), which only the game's own takes use: its pickup and loot-all top them up, Kiln's Items.Move does not
//   (lab dev31-review-autoloot-0213, review of 0.2.13). Other slots (equipment) give no room: a missed top-up there
//   costs a pickup left behind, never an overshoot.
//   Held: per item, over every slot the read returns (backpack, action bar, equipment): what carry limits count.
//   Max stack: what the world uses now (the caller decides: the game's own raised by the server's stack size); 0 =
//   not known, which gives no room.
namespace KilnAutoLoot.Core;

/// <summary>One non-empty slot of a player's inventories. <paramref name="Backpack"/>: a backpack slot (a Generic
/// inventory that is not an action bar, Core/BackpackRead). <paramref name="ActionBar"/>: a slot of one of the two action
/// bars.</summary>
public readonly record struct BagSlot(uint Item, int Count, bool Backpack, bool ActionBar = false);

public sealed class Bag
{
    private readonly Dictionary<uint, int> _held = new();
    private readonly Dictionary<uint, int> _room = new();
    private readonly Dictionary<uint, int> _barRoom = new();
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
            if (!s.Backpack && !s.ActionBar) continue;
            var max = maxStack(s.Item);
            if (max <= 1 || s.Count >= max) continue;
            var room = s.Backpack ? _room : _barRoom;
            room[s.Item] = room.GetValueOrDefault(s.Item) + (max - s.Count);
        }
    }

    private Bag(Bag from)
    {
        FreeSlots = from.FreeSlots;
        _maxStack = from._maxStack;
        _held = new Dictionary<uint, int>(from._held);
        _room = new Dictionary<uint, int>(from._room);
        _barRoom = new Dictionary<uint, int>(from._barRoom);
    }

    /// <summary>A copy to plan against: what is reserved on it does not touch this bag.</summary>
    public Bag Clone() => new(this);

    /// <summary>The max stack the world uses now for <paramref name="item"/>; 0 = not known.</summary>
    public int MaxStack(uint item) => Math.Max(0, _maxStack(item));

    /// <summary>How many of <paramref name="item"/> fit into <paramref name="slots"/> new stacks (an item whose max stack
    /// is not known counts one per slot).</summary>
    public long SlotCapacity(uint item, int slots) => slots <= 0 ? 0 : (long)slots * Math.Max(1, MaxStack(item));

    /// <summary>Free backpack slots now (after what this scan took); below 0 = not known.</summary>
    public int FreeSlots { get; private set; }

    /// <summary>Room left in the stacks of <paramref name="item"/> already carried in the backpack.</summary>
    public int Room(uint item) => _room.GetValueOrDefault(item);

    /// <summary>Room left in the stacks of <paramref name="item"/> on the action bar: what the game's own pickup and
    /// loot-all also top up (never Items.Move).</summary>
    public int BarRoom(uint item) => _barRoom.GetValueOrDefault(item);

    /// <summary>How many of <paramref name="item"/> the player carries.</summary>
    public int Held(uint item) => _held.GetValueOrDefault(item);

    /// <summary>How many the player carries of all these items together.</summary>
    public int HeldOf(ISet<uint> items)
    {
        long sum = 0;
        foreach (var (i, n) in _held) if (items.Contains(i)) sum += n;
        return (int)Math.Min(sum, int.MaxValue);
    }

    /// <summary>The scan sent a take whose items are not known (a creature pile with no known yield): it may start
    /// <paramref name="stacks"/> new stacks.</summary>
    public void TookNewStacks(int stacks)
    {
        if (stacks > 0 && FreeSlots >= 0) FreeSlots = Math.Max(0, FreeSlots - stacks);
    }

    /// <summary>The scan sent a take of <paramref name="count"/>: it fills carried stacks first (the backpack's, then with
    /// <paramref name="game"/>, a take the game itself hands over, the action bar's), the rest starts new stacks (each one
    /// a free slot less).</summary>
    public void Took(uint item, int count, bool game = false)
    {
        if (item == 0 || count <= 0) return;
        _held[item] = Held(item) + count;
        var rest = Fill(_room, item, count);
        if (game) rest = Fill(_barRoom, item, rest);
        if (rest <= 0 || FreeSlots < 0) return;
        var max = _maxStack(item);
        var stacks = max > 1 ? (rest + max - 1) / max : rest;
        FreeSlots = Math.Max(0, FreeSlots - stacks);
        if (max > 1 && rest % max != 0) _room[item] = Room(item) + (max - rest % max);
    }

    /// <summary>Takes up to <paramref name="count"/> of the room in <paramref name="rooms"/>; returns what is left over.</summary>
    private static int Fill(Dictionary<uint, int> rooms, uint item, int count)
    {
        var room = rooms.GetValueOrDefault(item);
        var into = Math.Min(room, count);
        if (into <= 0) return count;
        if (room - into > 0) rooms[item] = room - into;
        else rooms.Remove(item);
        return count - into;
    }
}
