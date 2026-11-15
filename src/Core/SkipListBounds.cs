// SPDX-License-Identifier: MIT
// How long a skip list may be. The server refuses a longer one (it keeps the player's previous settings), and the
// window never builds one, so the two always agree. An entry fits the longest internal item name with its "=" and room
// to spare; 256 entries are far more than a player writes by hand or picks in the window. A name part (a bare word) is
// searched in every item's names, "=Name" and "#Group" entries are looked up, so name parts get a bound of their own:
// the window never writes one, they come from editing the file by hand.
namespace KilnAutoLoot.Core;

public static class SkipListBounds
{
    public const int MaxEntries = 256;
    public const int MaxEntryLength = 96;
    public const int MaxNameParts = 32;

    /// <summary>The entries (comma-separated, trimmed, empty ones not counted) are at most <see cref="MaxEntries"/>,
    /// each at most <see cref="MaxEntryLength"/> characters, and at most <see cref="MaxNameParts"/> of them name parts.
    /// Stops at the first entry over.</summary>
    public static bool Within(string? raw) => Count(raw, MaxEntries + 1, out var over) <= MaxEntries && !over;

    /// <summary>The window may turn <paramref name="current"/> into <paramref name="next"/>: the result fits, or it is
    /// shorter (removing from a list that is over the bounds is always allowed).</summary>
    public static bool Accepts(string? current, string? next) =>
        Within(next) || Count(next, int.MaxValue, out _) < Count(current, int.MaxValue, out _);

    /// <summary>The non-empty entries, counted up to <paramref name="upTo"/> (then it stops).</summary>
    /// <param name="over">an entry is too long, or there are too many name parts.</param>
    private static int Count(string? raw, int upTo, out bool over)
    {
        over = false;
        if (string.IsNullOrEmpty(raw)) return 0;
        int count = 0, parts = 0, start = 0;
        while (start <= raw.Length && count < upTo)
        {
            var end = raw.IndexOf(',', start);
            if (end < 0) end = raw.Length;
            var span = raw.AsSpan(start, end - start).Trim(" \t\r\n");   // the blanks TextRules.Trim takes off
            if (span.Length > 0)
            {
                count++;
                if (span.Length > MaxEntryLength) over = true;
                if (span[0] is not ('=' or '#') && ++parts > MaxNameParts) over = true;
            }
            start = end + 1;
        }
        return count;
    }
}
