// SPDX-License-Identifier: MIT
using System.Text;

namespace KilnAutoLoot.Core;

/// <summary>Every item of the game with its names, menu group and stack size. Built from the player's own game data
/// (<see cref="ItemRules.Rows"/>); empty until that is done. <see cref="Fill"/> swaps the whole content at once, so a
/// reader on another thread (the window) sees the old table or the new one, never a mix. Lookups take the first row of
/// an id.</summary>
public sealed class ItemTable
{
    /// <summary>One row and its names and menu group lower-cased once with <see cref="Lower"/>.</summary>
    internal sealed record Entry(ItemRow Row, string LInternal, string LEnglish, string LGerman, string LMenuGroup)
    {
        /// <summary>The three lower-cased names in one string, split by <see cref="NameSeparator"/>: one search per row
        /// for a name part that does not hold the separator.</summary>
        public string LNames { get; } = LInternal + NameSeparator + LEnglish + NameSeparator + LGerman;

        /// <summary>A name part is in the internal, English or German name.</summary>
        public bool NamesContain(string lowerPart) =>
            lowerPart.Contains(NameSeparator)
                ? LInternal.Contains(lowerPart, StringComparison.Ordinal) || LEnglish.Contains(lowerPart, StringComparison.Ordinal) ||
                  LGerman.Contains(lowerPart, StringComparison.Ordinal)
                : LNames.Contains(lowerPart, StringComparison.Ordinal);
    }

    internal const char NameSeparator = '\u0001';

    private sealed class State
    {
        public readonly List<ItemRow> Rows = new();
        public readonly List<Entry> Entries = new();
        public readonly Dictionary<uint, Entry> ById = new();
        public readonly Dictionary<string, Entry> ByLowerInternal = new(StringComparer.Ordinal);
        // every row by each of its lower-cased names (internal, English, German) and by its lower-cased menu group, so
        // "=Name" and "#Group" entries are looked up instead of compared with every row
        public readonly Dictionary<string, List<Entry>> ByLowerName = new(StringComparer.Ordinal);
        public readonly Dictionary<string, List<Entry>> ByLowerGroup = new(StringComparer.Ordinal);
    }

    private static readonly IReadOnlyList<Entry> NoEntries = [];

    private static void AddTo(Dictionary<string, List<Entry>> map, string key, Entry e)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Entry>(1);
        if (list.Count == 0 || !ReferenceEquals(list[^1], e)) list.Add(e);   // a row whose names are equal counts once
    }

    private volatile State _state = new();

    private ItemTable() { }

    /// <summary>A table with no items yet.</summary>
    public static ItemTable Empty() => new();

    /// <summary>A table of these rows, in their order.</summary>
    public static ItemTable FromRows(IEnumerable<ItemRow> rows)
    {
        var t = new ItemTable();
        t.Fill(rows);
        return t;
    }

    /// <summary>Replaces the whole content with these rows.</summary>
    public void Fill(IEnumerable<ItemRow> rows)
    {
        var s = new State();
        foreach (var row in rows)
        {
            var e = new Entry(row, Lower(row.Internal), Lower(row.English), Lower(row.German), Lower(row.MenuGroup));
            s.Rows.Add(row);
            s.Entries.Add(e);
            s.ById.TryAdd(row.Id, e);
            s.ByLowerInternal.TryAdd(e.LInternal, e);
            AddTo(s.ByLowerName, e.LInternal, e);
            AddTo(s.ByLowerName, e.LEnglish, e);
            AddTo(s.ByLowerName, e.LGerman, e);
            AddTo(s.ByLowerGroup, e.LMenuGroup, e);
        }
        _state = s;
        Version++;
    }

    /// <summary>Counts the fills, so a cache built on an older content knows it is old.</summary>
    public int Version { get; private set; }

    internal IReadOnlyList<Entry> Entries => _state.Entries;

    /// <summary>The rows with this lower-cased internal, English or German name (<see cref="Lower"/>d already).</summary>
    internal IReadOnlyList<Entry> WithLowerName(string lowerName) =>
        _state.ByLowerName.TryGetValue(lowerName, out var l) ? l : NoEntries;

    /// <summary>The rows of this lower-cased menu group.</summary>
    internal IReadOnlyList<Entry> WithLowerGroup(string lowerGroup) =>
        _state.ByLowerGroup.TryGetValue(lowerGroup, out var l) ? l : NoEntries;

    /// <summary>A small tab-separated text form for test fixtures: <c>id, internal, English, German, lootable ("0" = no),
    /// category, menu group (empty = the category), GUID, max stack</c>, one row per line that starts with a digit.</summary>
    public static ItemTable Parse(TextReader text)
    {
        var rows = new List<ItemRow>();
        foreach (var line in TextRules.Lines(text))
        {
            if (line.Length == 0 || line[0] < '0' || line[0] > '9') continue;
            var c = TextRules.Columns(TextRules.Trim(line), 9);
            var category = TextRules.Trim(c[5]);
            var menuGroup = TextRules.Trim(c[6]);
            TextRules.TryParseGuid(TextRules.Trim(c[7]), out var guid);
            rows.Add(new ItemRow(
                Id: TextRules.ParseUInt(c[0]),
                Internal: TextRules.Trim(c[1]),
                English: TextRules.Trim(c[2]),
                German: TextRules.Trim(c[3]),
                Lootable: TextRules.Trim(c[4]) != "0",
                Category: category,
                MenuGroup: menuGroup.Length == 0 ? category : menuGroup,
                Guid: guid,
                OriginalMaxStack: (ushort)TextRules.ParseUInt(c[8])));
        }
        return FromRows(rows);
    }

    public IReadOnlyList<ItemRow> All => _state.Rows;

    public bool TryGet(uint id, out ItemRow row)
    {
        if (_state.ById.TryGetValue(id, out var e)) { row = e.Row; return true; }
        row = null!;
        return false;
    }

    /// <summary>The row whose internal name equals <paramref name="internalName"/> after Trim and <see cref="Lower"/>.</summary>
    public bool TryGetByInternal(string internalName, out ItemRow row)
    {
        if (_state.ByLowerInternal.TryGetValue(Lower(TextRules.Trim(internalName)), out var e)) { row = e.Row; return true; }
        row = null!;
        return false;
    }

    /// <summary>The display name: German if german and present, else English, else internal.
    /// An unknown id gives "" (no notice is shown for it).</summary>
    public string Display(uint id, bool german) => _state.ById.TryGetValue(id, out var e) ? DisplayOf(e.Row, german) : "";

    public static string DisplayOf(ItemRow row, bool german) =>
        german && row.German.Length > 0 ? row.German : row.English.Length > 0 ? row.English : row.Internal;

    /// <summary>The suggestions for a query (lootable rows only; 3+ characters or a group; at most <paramref name="max"/>).
    /// The 3-character minimum is the window's: this method answers any non-empty query.
    /// Pass 1: the display name or the internal name starts with the query; pass 2: display, internal, English or German
    /// name contains it. An empty query (a group chosen) lists the group sorted by display name, ä ö ü ß as a o u ss.
    /// <paramref name="menuGroup"/> is compared with <see cref="ItemRow.MenuGroup"/> exactly; null or "" = all groups.</summary>
    public IReadOnlyList<ItemRow> Suggest(string query, string? menuGroup, bool german, int max = 1024)
    {
        var q = Lower(TextRules.Trim(query ?? ""));
        var cat = menuGroup is null ? "" : TextRules.Trim(menuGroup);
        var result = new List<ItemRow>();
        if ((q.Length == 0 && cat.Length == 0) || max <= 0) return result;
        var entries = _state.Entries;
        for (int pass = 0; pass < 2 && result.Count < max; pass++)
        {
            foreach (var e in entries)
            {
                if (result.Count >= max) break;
                var it = e.Row;
                if (!it.Lootable || (cat.Length > 0 && it.MenuGroup != cat)) continue;
                var ldisp = german && e.LGerman.Length > 0 ? e.LGerman : e.LEnglish.Length > 0 ? e.LEnglish : e.LInternal;
                bool starts = ldisp.StartsWith(q, StringComparison.Ordinal) || e.LInternal.StartsWith(q, StringComparison.Ordinal);
                bool contains = ldisp.Contains(q, StringComparison.Ordinal) || e.LInternal.Contains(q, StringComparison.Ordinal) ||
                                e.LEnglish.Contains(q, StringComparison.Ordinal) || e.LGerman.Contains(q, StringComparison.Ordinal);
                if (pass == 0 ? !starts : (!contains || starts)) continue;
                result.Add(it);
            }
        }
        if (q.Length == 0)
            return result.OrderBy(r => SortKey(DisplayOf(r, german)), StringComparer.Ordinal).ToList();
        return result;
    }

    /// <summary>Lower: ASCII and Latin-1 upper case to lower case, independent of locale.
    /// A-Z and U+00C0..U+00DE except U+00D7 (×) become +0x20; nothing else changes (ß, Ÿ, Œ stay).</summary>
    public static string Lower(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
            else if (c >= 'À' && c <= 'Þ' && c != '×') sb.Append((char)(c + 0x20));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The sort key: <see cref="Lower"/>, then ä→a, ö→o, ü→u, ß→ss, so "Äste" sorts under A.</summary>
    public static string SortKey(string name)
    {
        var l = Lower(name);
        var sb = new StringBuilder(l.Length);
        foreach (char c in l)
        {
            switch (c)
            {
                case 'ä': sb.Append('a'); break;
                case 'ö': sb.Append('o'); break;
                case 'ü': sb.Append('u'); break;
                case 'ß': sb.Append("ss"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
