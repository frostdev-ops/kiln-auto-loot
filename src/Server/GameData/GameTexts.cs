// SPDX-License-Identifier: MIT
// The game's own texts in English and German, where the game has them: the player's game keeps one
// keen::LocaTagCollectionResource with a text blob per language (a dedicated server has neither). The blob is read
// through kiln.assets (Assets.ReadBlob) and parsed by Core.LocaBlob.
using System.Runtime.InteropServices;
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

internal static class GameTexts
{
    private const string CollectionType = "keen::LocaTagCollectionResource";

    /// <summary>The tables of the languages asked for ("En_Us", "De_De": the game's LanguageId names, compared without
    /// case and "_"/"-"), or null when this game has no texts. <paramref name="why"/> says what happened.</summary>
    public static Dictionary<string, Dictionary<uint, string>>? Load(string[] languages, out string why)
    {
        why = "";
        if (GameTypes.Find(CollectionType) is not { } type) { why = "the game has no text collection type"; return null; }
        var collection = ReflectedResources.All(type).FirstOrDefault();
        if (collection is null) { why = "the game has no text collection (a dedicated server)"; return null; }
        try
        {
            var v = new BlobView(collection.Bytes);
            var root = new BlobSpot(type.Resolved, 0);
            if (v.Find(root, "languages") is not { } list) { why = "the text collection has no language list"; return null; }
            var record = GameTypes.Find("keen::LocaTagResource");
            var size = record?.Size ?? LocaBlob.RecordSize;
            var idOff = record?.Resolved.Field("id")?.Offset ?? 0;
            var textOff = record?.Resolved.Field("text")?.Offset ?? 4;
            var result = new Dictionary<string, Dictionary<uint, string>>(StringComparer.Ordinal);
            var seen = new List<string>();
            foreach (var lang in v.Elements(list))
            {
                var name = v.Value(v.Find(lang, "language"))?.ToString() ?? "";
                seen.Add(name);
                var wanted = languages.FirstOrDefault(l => Same(l, name));
                if (wanted is null || v.Find(lang, "data.hash") is not { } hash || hash.Offset + 16 > v.Data.Length) continue;
                var content = MemoryMarshal.Read<ContentHash>(v.Data.AsSpan(hash.Offset, 16));
                var blob = Assets.ReadBlob(content);
                if (blob is null) continue;
                var table = LocaBlob.Parse(blob, size, idOff, textOff);
                if (table.Count > 0) result[wanted] = table;
            }
            why = $"languages in the game: {string.Join(" ", seen)}; read: {string.Join(" ", result.Select(r => $"{r.Key} ({r.Value.Count})"))}";
            return result.Count > 0 ? result : null;
        }
        catch (Exception e) when (e is InvalidDataException or KilnUnavailableException or NotSupportedException or InvalidOperationException)
        {
            why = $"the texts could not be read: {e.Message}";
            return null;
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.Replace("_", "").Replace("-", ""), b.Replace("_", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase);
}
