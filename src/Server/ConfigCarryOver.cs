// SPDX-License-Identifier: MIT
// Keys a newer version added to a settings file start from the older key they replace (Core/SettingsMigration). Kiln
// adds a missing key with its default as soon as it registers the file, so the file is read once before that, and the
// values are set right after it. Tick thread (where the configs are registered).
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal static class ConfigCarryOver
{
    /// <summary>The values to set in the config file <paramref name="id"/> (its file name without ".json") once it is
    /// registered; <paramref name="section"/> names the settings object inside it (null = the whole file). Nothing when
    /// there is no file yet (a first start) or it cannot be read.</summary>
    public static List<(string Path, bool Value)> Before(string id, string? section)
    {
        try
        {
            var path = Path.Combine(Config.Directory, id + ".json");
            return File.Exists(path) ? SettingsMigration.FromFile(File.ReadAllText(path), section) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Sets them in the registered file (one log line each).</summary>
    public static void After(ConfigFile config, List<(string Path, bool Value)> values, Action<string> log)
    {
        foreach (var (path, value) in values)
            log(config.TrySet(path, value, out var error)
                ? $"settings: {config.Id} {path} = {(value ? "true" : "false")}, carried over from the setting it replaces"
                : $"settings: {config.Id} {path} could not be carried over: {error}");
    }
}
