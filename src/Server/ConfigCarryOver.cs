// SPDX-License-Identifier: MIT
// Keys a newer version added to a settings file start from the older key they replace, and defaults a newer version
// changed move to the new value where the file still holds the old one (Core/SettingsMigration). Kiln adds a missing
// key with its default as soon as it registers the file, so the file is read once before that, and the values are set
// right after it. Tick thread (where the configs are registered).
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal static class ConfigCarryOver
{
    /// <summary>The values to set in the config file <paramref name="id"/> (its file name without ".json") once it is
    /// registered; <paramref name="section"/> names the settings object inside it (null = the whole file). Nothing when
    /// there is no file yet (a first start) or it cannot be read.</summary>
    public static List<(string Path, object Value)> Before(string id, string? section)
    {
        try
        {
            var path = Path.Combine(Config.Directory, id + ".json");
            if (!File.Exists(path)) return [];
            var text = File.ReadAllText(path);
            var list = SettingsMigration.FromFile(text, section).Select(x => (x.Path, (object)x.Value)).ToList();
            list.AddRange(SettingsMigration.MovedInFile(text, section));
            return list;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Sets them in the registered file (one log line each).</summary>
    public static void After(ConfigFile config, List<(string Path, object Value)> values, Action<string> log)
    {
        foreach (var (path, value) in values)
        {
            var text = value is bool b ? (b ? "true" : "false") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            log(config.TrySet(path, value, out var error)
                ? $"settings: {config.Id} {path} = {text}, carried over from an older version of the file"
                : $"settings: {config.Id} {path} could not be carried over: {error}");
        }
    }
}
