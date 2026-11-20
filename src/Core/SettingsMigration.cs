// SPDX-License-Identifier: MIT
// Settings keys a newer version added in place of an older one, so a file or message written before keeps its choice:
//   0.2.3  cropsInBase  (crops in or near a base) takes the old "crops" value: crops (now: out in the world) and crops
//                       in a base were one switch before;
//          critters     (the fishing bait swarms) takes the old "plants" value: they followed plants before.
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

    /// <summary>Adds what <see cref="Missing"/> finds to the object.</summary>
    public static void Apply(JsonObject? o)
    {
        if (o is null) return;
        foreach (var (key, value) in Missing(o)) o[key] = value;
    }

    /// <summary>A settings file's text (JSON with comments, as Kiln writes it): the config paths to set after Kiln read
    /// it, "key" or "&lt;section&gt;.key" for the settings object under <paramref name="section"/> (the server file's
    /// "defaults"). Nothing for no file, a file that does not read, or no such object.</summary>
    public static List<(string Path, bool Value)> FromFile(string? text, string? section)
    {
        var list = new List<(string, bool)>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        try
        {
            if (JsonNode.Parse(text, documentOptions: FileOptions) is not JsonObject root) return list;
            var o = section is null ? root : Get(root, section) as JsonObject;
            foreach (var (key, value) in Missing(o)) list.Add((section is null ? key : section + "." + key, value));
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            // not readable: Kiln says so itself; nothing to carry over
        }
        return list;
    }

    private static bool Has(JsonObject o, string key) => o.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Get(JsonObject o, string key) =>
        o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

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
