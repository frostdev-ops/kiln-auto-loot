// SPDX-License-Identifier: MIT
// The one JSON form of the settings that cross the network (PersonalMsg.Json, ServerInfoMsg.EffectiveJson and
// LimitsJson). Both sides use this class. Names are camelCase, the settings keys (radius,
// skipList, ...). Unknown properties are ignored; bad JSON reads as null. Ranges are not checked here:
// Core.SettingsRules.Clamp does that.
using System.Text.Json;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Net;

internal static class SettingsJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Write(PersonalSettings s) => JsonSerializer.Serialize(s, Options);

    public static PersonalSettings? Read(string json) => ReadAs<PersonalSettings>(json);

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
