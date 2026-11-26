// SPDX-License-Identifier: MIT
// The cache of what was read from the game: one JSON file per game data version in the mod's data directory, so only the
// first start after a game update reads the game's resources. The key says which game data the facts came from; a file
// with another key (or another format) is read as "no cache".
using System.Text.Json;

namespace KilnAutoLoot.Core;

public static class GameFactsJson
{
    /// <summary>Bump when the reader collects other facts: older cache files are then read again from the game.</summary>
    public const int Format = 2;   // 2 (0.2.13): templates carry YieldMax, the most of each item a default inventory gives

    private sealed record CacheFile(int Format, string Key, GameFacts Facts);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Write(string key, GameFacts facts) => JsonSerializer.Serialize(new CacheFile(Format, key, facts), Options);

    /// <summary>The facts in <paramref name="json"/> when it is a cache of this format for <paramref name="key"/>, else null
    /// (also for damaged text).</summary>
    public static GameFacts? Read(string json, string key)
    {
        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(json, Options);
            if (file is null || file.Format != Format || file.Key != key || file.Facts?.Templates is null || file.Facts.Items is null) return null;
            return file.Facts;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A file name for a key: the key's own characters where safe, so the directory shows which game it is for.</summary>
    public static string FileName(string key)
    {
        var safe = new string(key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' ? c : '_').ToArray());
        if (safe.Length > 80) safe = safe[..80];
        return $"game-data-{safe}.json";
    }
}
