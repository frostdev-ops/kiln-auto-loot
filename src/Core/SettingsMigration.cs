// SPDX-License-Identifier: MIT
// Settings keys a newer version added in place of an older one, so a file or message written before keeps its choice:
//   0.2.3  cropsInBase  (crops in or near a base) takes the old "crops" value: crops (now: out in the world) and crops
//                       in a base were one switch before;
//          critters     (the fishing bait swarms) takes the old "plants" value: they followed plants before.
// And defaults a newer version changed, for settings written before it (no settingsVersion) that still hold the old one:
//   0.2.8  line of sight decides what is in reach where it can be checked, the height window is the fallback:
//          heightAbove 4 -> 0, heightBelow 4 -> 0 (no limit), visibleOnly false -> true. A value the player changed
//          stays.
// Read where settings come from outside: the player's config file and the server file's defaults (before Kiln reads
// them, as Kiln writes the missing keys with their defaults at once), profiles.json and the settings a player's game
// sends. Pure: no Kiln calls.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KilnAutoLoot.Core;

public static class SettingsMigration
{
    /// <summary>The added key and the older key whose value it starts with.</summary>
    public static IReadOnlyList<(string Key, string From)> Added { get; } = [("cropsInBase", "crops"), ("critters", "plants")];

    /// <summary>The settingsVersion key: settings written from 0.2.8 on carry it (PersonalSettings.SettingsVersion).</summary>
    public const string VersionKey = "settingsVersion";

    /// <summary>The version of the defaults this build writes.</summary>
    public const int CurrentVersion = 2;

    /// <summary>Defaults 0.2.8 changed: the key, its old default and its new one.</summary>
    public static IReadOnlyList<(string Key, object Old, object New)> MovedDefaults { get; } =
        [("heightAbove", 4f, 0f), ("heightBelow", 4f, 0f), ("visibleOnly", false, true)];

    private static readonly JsonDocumentOptions FileOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The added keys a settings object lacks, each with the value of the key it came from (when that one is
    /// there as true or false). Keys match without regard to case, as the settings are read.</summary>
    public static List<(string Key, bool Value)> Missing(JsonObject? o)
    {
        var list = new List<(string, bool)>();
        if (o is null) return list;
        foreach (var (key, from) in Added)
            if (!Has(o, key) && Bool(Get(o, from)) is { } value) list.Add((key, value));
        return list;
    }

    /// <summary>The moved defaults (<see cref="MovedDefaults"/>) a settings object written before 0.2.8 (no
    /// settingsVersion) still holds, each with its new value. Keys match without regard to case.</summary>
    public static List<(string Key, object Value)> Moved(JsonObject? o)
    {
        var list = new List<(string, object)>();
        if (o is null || Has(o, VersionKey)) return list;
        foreach (var (key, old, value) in MovedDefaults)
        {
            var n = Get(o, key);
            var holdsOld = old switch
            {
                bool b => Bool(n) == b,
                float f => Number(n) is { } d && Math.Abs(d - f) < 1e-6,
                _ => false,
            };
            if (holdsOld) list.Add((key, value));
        }
        return list;
    }

    /// <summary>Adds what <see cref="Missing"/> finds to the object, and the new defaults <see cref="Moved"/>
    /// finds.</summary>
    public static void Apply(JsonObject? o)
    {
        if (o is null) return;
        var moved = Moved(o);
        foreach (var (key, value) in Missing(o)) o[key] = value;
        foreach (var (key, value) in moved)
        {
            var name = o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
            o[name] = value switch
            {
                bool b => JsonValue.Create(b),
                float f => JsonValue.Create(f),
                _ => JsonValue.Create(value.ToString()),
            };
        }
    }

    /// <summary>As <see cref="Moved"/> for a settings file's text: the config paths to set after Kiln read it.</summary>
    public static List<(string Path, object Value)> MovedInFile(string? text, string? section)
    {
        var list = new List<(string, object)>();
        if (Object(text, section) is not { } o) return list;
        foreach (var (key, value) in Moved(o)) list.Add((section is null ? key : section + "." + key, value));
        return list;
    }

    /// <summary>A settings file's text (JSON with comments, as Kiln writes it): the config paths to set after Kiln read
    /// it, "key" or "&lt;section&gt;.key" for the settings object under <paramref name="section"/> (the server file's
    /// "defaults"). Nothing for no file, a file that does not read, or no such object.</summary>
    public static List<(string Path, bool Value)> FromFile(string? text, string? section)
    {
        var list = new List<(string, bool)>();
        foreach (var (key, value) in Missing(Object(text, section))) list.Add((section is null ? key : section + "." + key, value));
        return list;
    }

    /// <summary>The settings object of a file's text (the whole file, or the object under <paramref name="section"/>);
    /// null for no text, text that does not read, or no such object.</summary>
    private static JsonObject? Object(string? text, string? section)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            if (JsonNode.Parse(text, documentOptions: FileOptions) is not JsonObject root) return null;
            return section is null ? root : Get(root, section) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;   // not readable: Kiln says so itself; nothing to carry over
        }
    }

    private static bool Has(JsonObject o, string key) => o.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Get(JsonObject o, string key) =>
        o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static double? Number(JsonNode? n)
    {
        try
        {
            return n?.GetValueKind() == JsonValueKind.Number ? n.GetValue<double>() : null;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static bool? Bool(JsonNode? n)
    {
        try
        {
            return n?.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
