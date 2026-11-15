// SPDX-License-Identifier: MIT
// The small text rules the data files and the ignore list share.
namespace KilnAutoLoot.Core;

internal static class TextRules
{
    private static readonly char[] Blank = { ' ', '\t', '\r', '\n' };

    /// <summary>Trim: space, tab, CR and LF at both ends.</summary>
    public static string Trim(string s) => s.Trim(Blank);

    /// <summary>strtoul(s, 10), as ids are read: leading blanks, then the leading digits; no digits = 0; too large = max.</summary>
    public static uint ParseUInt(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        if (i < s.Length && s[i] == '+') i++;
        ulong v = 0;
        bool any = false;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
        {
            any = true;
            v = v * 10 + (ulong)(s[i] - '0');
            if (v > uint.MaxValue) return uint.MaxValue;
        }
        return any ? (uint)v : 0;
    }

    /// <summary>A guid: 8-4-4-4-12 hex, '-', '{' and '}' dropped, 32 hex digits left.</summary>
    public static bool TryParseGuid(string text, out Guid guid)
    {
        guid = Guid.Empty;
        Span<char> hex = stackalloc char[32];
        int n = 0;
        foreach (char c in text)
        {
            if (c == '-' || c == '{' || c == '}') continue;
            if (n == 32 || !char.IsAsciiHexDigit(c)) return false;
            hex[n++] = c;
        }
        return n == 32 && Guid.TryParseExact(hex, "N", out guid);
    }

    /// <summary>Case-insensitive compare for ASCII letters only.</summary>
    public static bool AsciiEqualsIgnoreCase(string a, string b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i], y = b[i];
            if (x >= 'A' && x <= 'Z') x = (char)(x + 32);
            if (y >= 'A' && y <= 'Z') y = (char)(y + 32);
            if (x != y) return false;
        }
        return true;
    }

    /// <summary>The first <paramref name="count"/> tab columns of a trimmed line, cut
    /// (a missing column is empty; text after the last wanted column's tab is dropped).</summary>
    public static string[] Columns(string line, int count)
    {
        var cols = new string[count];
        int start = 0;
        for (int i = 0; i < count; i++)
        {
            if (start > line.Length) { cols[i] = ""; continue; }
            int tab = line.IndexOf('\t', start);
            cols[i] = tab < 0 ? line[start..] : line[start..tab];
            start = tab < 0 ? line.Length + 1 : tab + 1;
        }
        return cols;
    }

    public static IEnumerable<string> Lines(TextReader text)
    {
        for (string? line = text.ReadLine(); line != null; line = text.ReadLine()) yield return line;
    }
}
