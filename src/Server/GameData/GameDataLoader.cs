// SPDX-License-Identifier: MIT
// Builds the mod's Data from the player's own game: the cached facts when the game data did not change, else a fresh
// read (GameDataReader), then the pure classifiers of src/Core. Runs once per process on the tick thread (Kiln calls
// may not run on a thread of the mod's own): at ServerStartedEvent on a server world, at load (or a few seconds later)
// on a player's game. Later starts read the cache file only.
using System.Diagnostics;
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

internal static class GameDataLoader
{
    private static GameFacts? _facts;   // once per process
    private static string _source = "";

    /// <summary>The facts were read (or loaded from the cache) in this process.</summary>
    public static bool HasFacts => _facts is not null;

    /// <summary>Fills <paramref name="data"/> from the game; false when the game's resources cannot be read here yet.
    /// <paramref name="log"/> gets one line on what was done and how long it took.</summary>
    public static bool TryFill(Data data, string dataDirectory, Action<string> log)
    {
        if (_facts is null)
        {
            if (!GameDataReader.Available) return false;
            var sw = Stopwatch.StartNew();
            var key = CacheKey();
            var path = Path.Combine(dataDirectory, GameFactsJson.FileName(key));
            var cached = ReadCache(path, key);
            if (cached is not null && cached.Items.Count > 0)
            {
                _facts = cached;
                _source = $"cache {Path.GetFileName(path)} in {sw.Elapsed.TotalMilliseconds:0} ms";
            }
            else
            {
                var facts = GameDataReader.Read(out var report);
                if (facts.Templates.Count == 0 || facts.Items.Count == 0) return false;   // the game has not loaded its data yet
                _facts = facts;
                _source = $"read from the game in {sw.Elapsed.TotalMilliseconds:0} ms (templates {report.TemplateMs:0} ms, " +
                          $"items {report.ItemMs:0} ms, names {report.NamesMs:0} ms; names from {report.Names})";
                WriteCache(dataDirectory, path, key, facts, log);
            }
        }
        var t = Stopwatch.StartNew();
        data.Fill(GameRules.Classify(_facts));
        log($"game data: {_facts.Templates.Count} templates, {_facts.Items.Count} items, {_source}; " +
            $"rules in {t.Elapsed.TotalMilliseconds:0} ms: {data.Templates.Count} templates taken, {data.Items.All.Count} items, " +
            $"{data.Quest.Count} quest items, {data.Smash.Count} smash targets");
        return true;
    }

    /// <summary>What changes with the game data: the game's executable (size and time), Kiln's version, which side reads
    /// (a player's game has texts a server lacks), the reader's format and the list of template and item GUIDs.</summary>
    public static string CacheKey()
    {
        var exe = "";
        try
        {
            if (Environment.ProcessPath is { } p && File.Exists(p))
            {
                var f = new FileInfo(p);
                exe = $"{f.Length}-{f.LastWriteTimeUtc.Ticks:x}";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var side = Kiln.Client.IsDedicatedServer ? "server" : "game";
        return $"v{GameFactsJson.Format}-{side}-{Kiln.Game.KilnVersion}-{exe}-{GameDataReader.Fingerprint()}";
    }

    private static GameFacts? ReadCache(string path, string key)
    {
        try { return File.Exists(path) ? GameFactsJson.Read(File.ReadAllText(path), key) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void WriteCache(string dir, string path, string key, GameFacts facts, Action<string> log)
    {
        try
        {
            Directory.CreateDirectory(dir);
            // one cache per side: older files of this side are for another game version
            var side = key.Split('-');
            foreach (var old in Directory.EnumerateFiles(dir, $"game-data-{side[0]}-{side[1]}-*.json"))
                if (old != path) File.Delete(old);
            File.WriteAllText(path + ".tmp", GameFactsJson.Write(key, facts));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"game data: the cache could not be written ({e.Message}); the next start reads the game again");
        }
    }

    /// <summary>The whole classification as text, for checking (ServerSettings.CounterLog).</summary>
    public static void WriteDump(Data data, string dataDirectory, Action<string> log)
    {
        if (_facts is null) return;
        try
        {
            var path = Path.Combine(dataDirectory, "kiln-auto-loot-classification.txt");
            File.WriteAllText(path, ClassificationDump.Text(_facts, new Classification(data.Templates.All, data.Items.All,
                data.Quest.All.ToList(), data.Smash.All)));
            log($"game data: classification written to {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"game data: the classification could not be written ({e.Message})");
        }
    }
}
