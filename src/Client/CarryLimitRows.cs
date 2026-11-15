// SPDX-License-Identifier: MIT
// The carry-limits text as rows for the window: "=Material_T1_Twigs:500, #Materials:2000" is two rows, each an entry
// (an item's internal name with "=", a group key with "#", or a name part typed by hand) and a number (0 = carry none). The window only ever writes internal names
// and group keys: a dedicated server knows the internal names only, and carry limits carry no item ids. A piece the
// rows cannot read (typed by hand) is kept as it was and written back unchanged, so editing other rows never loses it.
// The written text keeps to the same bounds as the skip list (SkipListBounds). Pure.
using System.Globalization;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

/// <param name="Entry">"=Name" or "#Group" (or, for a piece that is not understood, the piece as written).</param>
/// <param name="Amount">at most this many carried; -1 = the piece was not understood.</param>
internal sealed record CarryLimit(string Entry, int Amount)
{
    public bool Understood => Amount >= 0;
}

internal static class CarryLimitRows
{
    /// <summary>The largest limit the window writes: more than any bag holds (the server takes up to
    /// CarryLimits.MaxLimit).</summary>
    public const int MaxAmount = 999_999;

    /// <summary>What a new row starts with.</summary>
    public const int DefaultAmount = 500;

    public static List<CarryLimit> Parse(string? raw)
    {
        var rows = new List<CarryLimit>();
        if (string.IsNullOrEmpty(raw)) return rows;
        foreach (var part in raw.Split(','))
        {
            var piece = TextRules.Trim(part);
            if (piece.Length == 0) continue;
            var colon = piece.LastIndexOf(':');
            if (colon > 0 && ParseAmount(piece[(colon + 1)..]) is { } n)
            {
                var key = TextRules.Trim(piece[..colon]);
                var name = key[0] is '=' or '#' ? TextRules.Trim(key[1..]) : key;
                if (name.Length > 0)
                {
                    rows.Add(new CarryLimit(key[0] is '=' or '#' ? key[0] + name : name, n));
                    continue;
                }
            }
            rows.Add(new CarryLimit(piece, -1));
        }
        return rows;
    }

    public static string Write(IEnumerable<CarryLimit> rows) =>
        string.Join(", ", rows.Select(r => r.Understood ? r.Entry + ":" + r.Amount.ToString(CultureInfo.InvariantCulture) : r.Entry));

    /// <summary>A typed number: digits only (blanks trimmed), clamped to 0..<see cref="MaxAmount"/>; null when it is no
    /// number.</summary>
    public static int? ParseAmount(string? text)
    {
        var t = TextRules.Trim(text ?? "");
        if (t.Length == 0 || !t.All(char.IsAsciiDigit)) return null;
        if (!long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return MaxAmount;
        return (int)Math.Min(v, MaxAmount);
    }

    public static string ItemEntry(ItemRow row) => "=" + row.Internal;

    public static string GroupEntry(string menuGroup) => "#" + menuGroup;

    /// <summary>The text with a row for <paramref name="entry"/> (an existing row for it, any case, gets the amount instead).</summary>
    public static string With(string? raw, string entry, int amount)
    {
        var rows = Parse(raw);
        var i = rows.FindIndex(r => r.Understood && TextRules.AsciiEqualsIgnoreCase(r.Entry, entry));
        var n = Math.Clamp(amount, 0, MaxAmount);
        if (i >= 0) rows[i] = rows[i] with { Amount = n };   // the row keeps its spelling
        else rows.Add(new CarryLimit(entry, n));
        return Write(rows);
    }

    public static string WithAmount(string? raw, int index, int amount)
    {
        var rows = Parse(raw);
        if (index < 0 || index >= rows.Count || !rows[index].Understood) return Write(rows);
        rows[index] = rows[index] with { Amount = Math.Clamp(amount, 0, MaxAmount) };
        return Write(rows);
    }

    public static string WithoutAt(string? raw, int index)
    {
        var rows = Parse(raw);
        if (index >= 0 && index < rows.Count) rows.RemoveAt(index);
        return Write(rows);
    }

    public static bool Has(string? raw, string entry) =>
        Parse(raw).Any(r => r.Understood && TextRules.AsciiEqualsIgnoreCase(r.Entry, entry));
}
