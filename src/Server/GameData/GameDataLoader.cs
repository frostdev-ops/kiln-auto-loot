// SPDX-License-Identifier: MIT
// Builds the mod's Data from the player's own game: the cached facts when the game data did not change, else a fresh
// read (GameDataReader), then the pure classifiers of src/Core. Once per process, in steps (StagedLoad) so the game's
// thread never waits for the disk:
//   - a worker computes the cache key and reads the cache, else the game's resources (kiln.assets' resource reads and
//     kiln.types answer on any thread);
//   - the game's thread names the items after a fresh read: GameTexts reads the text blobs with Assets.ReadBlob, which
//     may ask the game for its content index, and only the game's own threads may do that;
//   - a worker classifies; the game's thread fills Data; after a fresh read a worker writes the cache.
// A player's game starts it at load and polls it each tick (AutoLootMod); a server world waits for it at
// ServerStartedEvent, whose handlers expect a ready Data. Later starts read the cache file only.
using System.Diagnostics;
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

internal static class GameDataLoader
{
    private static readonly object Gate = new();
    private static StagedLoad<Classification>? _load;   // once per process
    private static string _reported = "";               // the last failure logged, so a retry every 2 s logs it once
    private static string _namesFrom = "";              // where the names came from (the naming step)

    /// <summary>The game data as read (the probe's comps command), null until it is in.</summary>
    public static GameFacts? Facts => _load?.Facts;

    /// <summary>A step runs on a worker now: poll each tick rather than every few seconds.</summary>
    public static bool Busy => _load?.Running is not null;

    /// <summary>Moves the load on without waiting; fills <paramref name="data"/> when the data is in. False while it is
    /// not (yet), or when the game's resources cannot be read here yet. The game's thread.</summary>
    public static bool Poll(Data data, string dataDirectory, Action<string> log, Action<Exception> error) =>
        Take(Load(dataDirectory, log).Step(), data, log, error);

    /// <summary>As <see cref="Poll"/>, but waits for the worker steps on this thread (a server world's
    /// start).</summary>
    public static bool Finish(Data data, string dataDirectory, Action<string> log, Action<Exception> error) =>
        Take(Load(dataDirectory, log).Finish(), data, log, error);

    private static StagedLoad<Classification> Load(string dataDirectory, Action<string> log)
    {
        lock (Gate)
        {
            if (_load is not null) return _load;
            // the game's thread: which side reads and Kiln's version; the rest of the key needs the disk and the lists
            var side = Kiln.Client.IsDedicatedServer ? "server" : "game";
            var kiln = Kiln.Game.KilnVersion;
            return _load = new StagedLoad<Classification>(
                () => GameDataReader.Available,
                () => Read(dataDirectory, side, kiln),
                facts =>
                {
                    var (named, how) = ItemNames.Add(facts.Items);
                    _namesFrom = how;
                    return facts with { Items = named };
                },
                GameRules.Classify,
                (key, facts) => WriteCache(dataDirectory, key, facts, log));
        }
    }

    private static bool Take(LoadState state, Data data, Action<string> log, Action<Exception> error)
    {
        var load = _load!;
        if (state == LoadState.Failed && load.Error is { } e)
        {
            if (e.Message != _reported) error(e);
            _reported = e.Message;
            return false;
        }
        if (state != LoadState.Ready) return false;
        var t = Stopwatch.StartNew();
        lock (Gate)   // a hosted game: the player's tick and the server world's start may both get here
        {
            if (data.Ready) return true;
            data.Fill(load.Result!);
        }
        var facts = load.Facts!;
        var names = load.NameMs > 0 ? $"names in {load.NameMs:0} ms from {_namesFrom}; " : "";
        log($"game data: {facts.Templates.Count} templates, {facts.Items.Count} items, {load.Source}; {names}" +
            $"rules in {load.ClassifyMs:0} ms, filled in {t.Elapsed.TotalMilliseconds:0} ms: " +
            $"{data.Templates.Count} templates taken, {data.Items.All.Count} items, {data.Quest.Count} quest items, " +
            $"{data.Smash.Count} smash targets");
        return true;
    }

    /// <summary>The worker's step: the cache file for this game data, else the game's resources (no names
    /// yet).</summary>
    private static ReadResult Read(string dataDirectory, string side, string kiln)
    {
        var sw = Stopwatch.StartNew();
        var key = CacheKey(side, kiln);
        var path = Path.Combine(dataDirectory, GameFactsJson.FileName(key));
        var cached = ReadCache(path, key);
        if (cached is not null && cached.Items.Count > 0)
            return new ReadResult(cached, true,
                $"cache {Path.GetFileName(path)} in {sw.Elapsed.TotalMilliseconds:0} ms", key);
        var facts = GameDataReader.Read(out var report);
        if (facts.Templates.Count == 0 || facts.Items.Count == 0)
            return new ReadResult(null, false, "");   // the game has not loaded its data yet
        return new ReadResult(facts, false, $"read from the game in {sw.Elapsed.TotalMilliseconds:0} ms on a worker " +
            $"(templates {report.TemplateMs:0} ms, items {report.ItemMs:0} ms)", key);
    }

    /// <summary>What changes with the game data: the game's executable (size and time), Kiln's version, which side reads
    /// (a player's game has texts a server lacks), the reader's format and the list of template and item GUIDs.</summary>
    private static string CacheKey(string side, string kiln)
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
        return $"v{GameFactsJson.Format}-{side}-{kiln}-{exe}-{GameDataReader.Fingerprint()}";
    }

    private static GameFacts? ReadCache(string path, string key)
    {
        try { return File.Exists(path) ? GameFactsJson.Read(File.ReadAllText(path), key) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A worker, after a fresh read (the cache missed: a new game or Kiln version, or other game
    /// data).</summary>
    private static void WriteCache(string dir, string key, GameFacts facts, Action<string> log)
    {
        var path = Path.Combine(dir, GameFactsJson.FileName(key));
        try
        {
            var sw = Stopwatch.StartNew();
            Directory.CreateDirectory(dir);
            // one cache per side: older files of this side are for another game version
            var side = key.Split('-');
            foreach (var old in Directory.EnumerateFiles(dir, $"game-data-{side[0]}-{side[1]}-*.json"))
                if (old != path) File.Delete(old);
            var text = GameFactsJson.Write(key, facts);
            File.WriteAllText(path + ".tmp", text);
            File.Move(path + ".tmp", path, overwrite: true);
            log($"game data: cache {Path.GetFileName(path)} written on a worker ({text.Length / 1024} KB in " +
                $"{sw.Elapsed.TotalMilliseconds:0} ms)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"game data: the cache could not be written ({e.Message}); the next start reads the game again");
        }
    }

    /// <summary>The whole classification as text, for checking (ServerSettings.CounterLog). The text is made here; a
    /// worker writes it.</summary>
    public static void WriteDump(Data data, string dataDirectory, Action<string> log)
    {
        if (_load?.Facts is not { } facts) return;
        var text = ClassificationDump.Text(facts, new Classification(data.Templates.All, data.Items.All,
            data.Quest.All.ToList(), data.Smash.All));
        var path = Path.Combine(dataDirectory, "kiln-auto-loot-classification.txt");
        Task.Run(() =>
        {
            try
            {
                File.WriteAllText(path, text);
                log($"game data: classification written to {path}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"game data: the classification could not be written ({e.Message})");
            }
        });
    }
}
