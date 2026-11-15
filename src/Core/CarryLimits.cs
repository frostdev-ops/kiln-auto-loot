// SPDX-License-Identifier: MIT
// carryLimits: "=Name:500, #Group:2000, word:300". Each entry is an item set written the way the skip list writes one
// (the same lookups and case rules: IgnoreList over the part before the last ':'), then the most the player wants to
// carry. "=Name" limits each item it names on its own; "#Group" and a bare name part limit all their items together.
// An entry without a whole number after its ':' counts for nothing. Bounded like the skip list (SkipListBounds).
namespace KilnAutoLoot.Core;

public sealed class CarryLimits
{
    /// <summary>The largest limit an entry may set; bigger numbers are cut to it.</summary>
    public const int MaxLimit = 10_000_000;

    private readonly record struct Entry(HashSet<uint> Items, int Limit, bool PerItem);

    private readonly List<Entry> _entries = new();

    public static readonly CarryLimits None = new("", ItemTable.Empty());

    public CarryLimits(string? raw, ItemTable items)
    {
        Raw = raw ?? "";
        foreach (var part in Raw.Split(','))
        {
            var entry = TextRules.Trim(part);
            var colon = entry.LastIndexOf(':');
            if (colon <= 0) continue;
            var key = TextRules.Trim(entry[..colon]);
            var number = TextRules.Trim(entry[(colon + 1)..]);
            if (key.Length == 0 || !long.TryParse(number, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var limit)) continue;
            var set = new IgnoreList(key, items).Ids;
            if (set.Count == 0) continue;
            _entries.Add(new Entry(new HashSet<uint>(set), (int)Math.Min(limit, MaxLimit), key[0] == '='));
        }
    }

    public string Raw { get; }

    /// <summary>No entry matched an item.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>How many more of <paramref name="item"/> the player may take with this bag (the tightest entry);
    /// int.MaxValue = no limit names it.</summary>
    public int Left(uint item, Bag bag)
    {
        var left = int.MaxValue;
        foreach (var e in _entries)
        {
            if (!e.Items.Contains(item)) continue;
            var held = e.PerItem ? bag.Held(item) : bag.HeldOf(e.Items);
            left = Math.Min(left, Math.Max(0, e.Limit - held));
        }
        return left;
    }

    /// <summary>A limit names the item.</summary>
    public bool Names(uint item)
    {
        foreach (var e in _entries) if (e.Items.Contains(item)) return true;
        return false;
    }
}
