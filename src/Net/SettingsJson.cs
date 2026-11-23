// SPDX-License-Identifier: MIT
// The one JSON form of the settings that cross the network (PersonalMsg.Json, ServerInfoMsg.EffectiveJson and
// LimitsJson). Both sides use this class. Names are camelCase, the settings keys (radius,
// skipList, ...). Unknown properties are ignored; bad JSON reads as null. Ranges are not checked here:
// Core.SettingsRules.Clamp does that.
using System.Text.Json;
using System.Text.Json.Nodes;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Net;

internal static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = SettingsRules.JsonOptions;

    public static string Write(PersonalSettings s) => JsonSerializer.Serialize(s, Options);

    /// <summary>A player's settings, or what a server applies for one; keys an older game or server does not send yet
    /// start from the ones they replace (SettingsMigration.ApplyAdded). Defaults a newer version moved are left as
    /// sent: an older game's settings apply as its window shows them, and a newer game shows what an older server
    /// applies.</summary>
    public static PersonalSettings? Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return ReadAs<PersonalSettings>(json);
            SettingsMigration.ApplyAdded(o);
            return o.Deserialize<PersonalSettings>(Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static string WriteLimits(ServerLimits limits) => JsonSerializer.Serialize(limits, Options);

    public static ServerLimits? ReadLimits(string json) => ReadAs<ServerLimits>(json);

    private static T? ReadAs<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
