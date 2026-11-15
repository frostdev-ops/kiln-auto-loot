// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>What an entry of the chosen list is, for the F8 list.</summary>
public enum IgnoreEntryKind
{
    /// <summary>"#Group" of a group in <see cref="Texts.ItemGroups"/>: "All: &lt;group&gt;" + the entry in grey.</summary>
    Group,
    /// <summary>"=Name" that matches no item: the entry in red + "(unknown: no item of the game has that name)".</summary>
    Unknown,
    /// <summary>The entry (without "=") is an internal item name: display name + internal name in grey.</summary>
    Item,
    /// <summary>Another "=" entry that matches (an English or German name): its text without "=".</summary>
    Exact,
    /// <summary>A bare word (or an unknown "#Group"): its text + "(name part)".</summary>
    NamePart,
}

/// <param name="Text">what the list shows: the entry, or for Item/Exact the part after "=".</param>
/// <param name="Item">the row for <see cref="IgnoreEntryKind.Item"/>.</param>
/// <param name="GroupKey">the <see cref="Texts.ItemGroups"/> key for <see cref="IgnoreEntryKind.Group"/>.</param>
public sealed record IgnoreEntryInfo(string Entry, IgnoreEntryKind Kind, string Text, ItemRow? Item, string? GroupKey);

/// <summary>The ignore list. Immutable: the editing methods return the new raw value; build a new
/// IgnoreList from it.</summary>
public sealed class IgnoreList
{
    private readonly ItemTable _items;
    private readonly HashSet<uint> _ids = new();
    private readonly List<string> _entries = new();
    private readonly List<string> _unknown = new();

    public IgnoreList(string raw, ItemTable items)
    {
        Raw = raw ?? "";
        _items = items;
        var seen = new HashSet<string>(StringComparer.Ordinal);   // an entry written twice is matched once
        var unknownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in Raw.Split(','))
        {
            var entry = TextRules.Trim(part);   // the entry as written
            if (entry.Length == 0) continue;
            _entries.Add(entry);
            var p = ItemTable.Lower(entry);
            bool exact = p[0] == '=', group = p[0] == '#';
            if (exact || group) p = TextRules.Trim(p[1..]);
            var key = (exact ? "=" : group ? "#" : " ") + p;
            if (!seen.Add(key))
            {
                if (unknownKeys.Contains(key)) _unknown.Add(entry);   // every copy of an unknown entry is shown red
                continue;
            }
            bool matched = false;
            if (p.Length > 0)
            {
                // every item, lootable or not; "=" and "#" are looked up, a name part is searched in every name
                if (group || exact)
                    foreach (var e in group ? items.WithLowerGroup(p) : items.WithLowerName(p)) { matched = true; _ids.Add(e.Row.Id); }
                else
                    foreach (var e in items.Entries)
                        if (e.NamesContain(p)) { matched = true; _ids.Add(e.Row.Id); }
            }
            if (exact && !matched)
            {
                _unknown.Add(entry);
                unknownKeys.Add(key);
            }
        }
    }

    private IgnoreList(IgnoreList from, IEnumerable<uint> moreIds)
    {
        Raw = from.Raw;
        _items = from._items;
        _ids = new HashSet<uint>(from._ids);
        _ids.UnionWith(moreIds);
        _entries = from._entries;
        _unknown = from._unknown;
    }

    /// <summary>This list with more ignored item ids: the ids a player's game matched the same raw value to with its own
    /// item names (the server may know internal names only). Entries, unknown entries and the editing methods stay this
    /// list's.</summary>
    public IgnoreList WithIds(IEnumerable<uint> ids) => new(this, ids);


    public string Raw { get; }

    /// <summary>The entries as written (trimmed, empty ones dropped), in order.</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Ignored item ids (all of them).</summary>
    public IReadOnlyCollection<uint> Ids => _ids;

    public bool Contains(uint itemId) => _ids.Contains(itemId);

    /// <summary>"=" entries that match no item (shown red in F8, logged).</summary>
    public IReadOnlyList<string> UnknownEntries => _unknown;

    /// <summary>An "=" entry whose name matches no item's internal, English or German name.</summary>
    public bool IsUnknown(string entry)
    {
        var p = ItemTable.Lower(TextRules.Trim(entry ?? ""));
        if (p.Length == 0 || p[0] != '=') return false;
        p = TextRules.Trim(p[1..]);
        return p.Length == 0 || _items.WithLowerName(p).Count == 0;
    }

    /// <summary>an entry without its leading "=" equals the internal name, ASCII case-insensitive.</summary>
    public bool IsChosen(ItemRow row)
    {
        foreach (var e in _entries) if (TextRules.AsciiEqualsIgnoreCase(StripExact(e), row.Internal)) return true;
        return false;
    }

    /// <summary>"#&lt;menuGroup&gt;" is in the list (ASCII case-insensitive).</summary>
    public bool IsGroupChosen(string menuGroup)
    {
        var g = "#" + menuGroup;
        foreach (var e in _entries) if (TextRules.AsciiEqualsIgnoreCase(e, g)) return true;
        return false;
    }

    /// <summary>The raw value after adding or removing an entry, entries joined with ", ".
    /// Adds "=&lt;Internal&gt;"; when the row is already chosen the list stays as it is.</summary>
    public string WithExact(ItemRow row) => IsChosen(row) ? Join(_entries) : Join(_entries.Append("=" + row.Internal));

    /// <summary>Adds "#&lt;menuGroup&gt;" unless it is there.</summary>
    public string WithGroup(string menuGroup) =>
        IsGroupChosen(menuGroup) ? Join(_entries) : Join(_entries.Append("#" + menuGroup));

    /// <summary>Removes every entry equal to <paramref name="entry"/> (trimmed, ASCII case-insensitive).</summary>
    public string Without(string entry)
    {
        var t = TextRules.Trim(entry ?? "");
        return Join(_entries.Where(e => !TextRules.AsciiEqualsIgnoreCase(e, t)));
    }

    /// <summary>Removes the entry at <paramref name="index"/> of <see cref="Entries"/> (the "x" button).</summary>
    public string WithoutAt(int index) => Join(_entries.Where((_, i) => i != index));

    /// <summary>Removes every entry that makes <paramref name="row"/> chosen.</summary>
    public string WithoutItem(ItemRow row) =>
        Join(_entries.Where(e => !TextRules.AsciiEqualsIgnoreCase(StripExact(e), row.Internal)));

    /// <summary>A click on a suggestion: chosen = remove it, else add "=&lt;Internal&gt;".</summary>
    public string Toggled(ItemRow row) => IsChosen(row) ? WithoutItem(row) : WithExact(row);

    /// <summary>The group button: chosen = remove "#&lt;group&gt;", else add it.</summary>
    public string ToggledGroup(string menuGroup) => IsGroupChosen(menuGroup) ? Without("#" + menuGroup) : WithGroup(menuGroup);

    /// <summary>How the chosen list shows <paramref name="entry"/> (the first match wins).</summary>
    public IgnoreEntryInfo Classify(string entry)
    {
        var e = TextRules.Trim(entry ?? "");
        if (e.Length > 0 && e[0] == '#')
            foreach (var g in Texts.ItemGroups)
                if (g.Key.Length > 0 && TextRules.AsciiEqualsIgnoreCase(g.Key, e[1..]))
                    return new(e, IgnoreEntryKind.Group, e, null, g.Key);
        if (IsUnknown(e)) return new(e, IgnoreEntryKind.Unknown, e, null, null);
        var name = StripExact(e);
        if (_items.TryGetByInternal(name, out var row)) return new(e, IgnoreEntryKind.Item, name, row, null);
        if (e.Length > 0 && e[0] == '=') return new(e, IgnoreEntryKind.Exact, name, null, null);
        return new(e, IgnoreEntryKind.NamePart, e, null, null);
    }

    private static string StripExact(string e) => e.Length > 0 && e[0] == '=' ? e[1..] : e;

    private static string Join(IEnumerable<string> entries) => string.Join(", ", entries);
}
