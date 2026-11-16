// SPDX-License-Identifier: MIT
// The keys of the profiles and of gather focus. Ctrl+F10 goes to the next profile (not bare F10: XHL-Core opens its
// window on F10, and Kiln keeps a bound key from the game and the other loaders) and F11 turns gather focus on or off;
// a profile may have a key of its own (registered with Kiln while it is set, so it can be rebound on Kiln's Keys page
// like any other). The key texts are Kiln key names ("Ctrl+1", "F7"); whether Kiln knows a name is Kiln's to say
// (KeyChord.TryParse, in the window). Pure.
namespace KilnAutoLoot.Client;

internal static class ProfileKeys
{
    public const string DefaultNextProfileKey = "Ctrl+F10";
    public const string DefaultFocusKey = "F11";
    public const string NextProfileId = "kiln-auto-loot.next-profile";
    public const string FocusId = "kiln-auto-loot.focus";
    private const string ProfilePrefix = "kiln-auto-loot.profile.";

    /// <summary>The Kiln binding id of a profile's own key.</summary>
    public static string BindingId(string profileId) => ProfilePrefix + profileId;

    /// <summary>A key text as stored: blanks taken off, also around "+"; empty or "None" = no key (null).</summary>
    public static string? Normalize(string? key)
    {
        if (key is null) return null;
        var parts = key.Split('+').Select(p => p.Trim()).ToArray();
        var t = string.Join("+", parts);
        if (t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return t;   // "Ctrl+" stays as typed: Kiln refuses it when it is bound
    }

    /// <summary>Two key texts name the same key (case and blanks aside, modifiers in any order).</summary>
    public static bool Same(string? a, string? b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (x is null || y is null) return false;
        return Canonical(x) == Canonical(y);
    }

    private static string Canonical(string key)
    {
        var parts = key.Split('+');
        var main = parts[^1].ToUpperInvariant();
        var mods = parts[..^1].Select(m => m.ToUpperInvariant() switch
        {
            "CONTROL" => "CTRL",
            var other => other,
        }).OrderBy(m => m, StringComparer.Ordinal);
        return string.Join("+", mods.Append(main));
    }

    /// <summary>A profile id as the file and the binding id use it: lower-case a-z, 0-9 and "-", 1 to 32 characters;
    /// null when the text has none of those.</summary>
    public static string? SafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var chars = id.Trim().ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-').Take(32).ToArray();
        return chars.Length == 0 ? null : new string(chars);
    }

    /// <summary>The key is one of the mod's other keys (named as they are bound now).</summary>
    public static bool IsTaken(string? key, IEnumerable<string> otherKeys) => otherKeys.Any(k => Same(k, key));
}
