// SPDX-License-Identifier: MIT
// English and German item names for the facts read from the game, from the best source this process has:
//   1. the game's own texts (a player's game, also while it hosts): English and German, by each item's name tag;
//   2. Kiln's item catalog (Items.All on a server with kiln.items): English only;
//   3. Kiln's text catalog (Texts, when the owner installed one on the server): English only.
// A dedicated server has no German texts: its items keep German "" and the names fall back to English.
using Kiln;
using KilnAutoLoot.Core;
using Texts = Kiln.Texts;

namespace KilnAutoLoot.Server.GameData;

internal static class ItemNames
{
    /// <summary>The items with names filled in, and a line on where the names came from.</summary>
    public static (List<ItemFacts> Items, string How) Add(List<ItemFacts> items)
    {
        var texts = GameTexts.Load(["En_Us", "De_De"], out var why);
        if (texts is not null)
        {
            texts.TryGetValue("En_Us", out var en);
            texts.TryGetValue("De_De", out var de);
            return (items.Select(i => Named(i, Text(en, i), Text(de, i))).ToList(), $"the game's texts ({why})");
        }
        var catalog = KilnCatalogNames();
        if (catalog.Count > 0)
            return (items.Select(i => Named(i, catalog.GetValueOrDefault(i.Id, ""), "")).ToList(),
                $"Kiln's item catalog, English only ({why})");
        var viaTexts = 0;
        var named = items.Select(i =>
        {
            var en = i.Value("nameTag") is { } t && Guid.TryParse(t, out var g) ? Texts.GetPlain(g) ?? "" : "";
            if (en.Length > 0) viaTexts++;
            return Named(i, en, "");
        }).ToList();
        return (named, viaTexts > 0 ? $"Kiln's text catalog, English only ({why})" : $"no names: internal names only ({why}; {Texts.Status})");
    }

    private static string Text(Dictionary<uint, string>? table, ItemFacts i) =>
        table is not null && i.Value("nameTag") is { } t && Guid.TryParse(t, out var g) && table.TryGetValue(LocaBlob.TagOf(g), out var s)
            ? LocaBlob.Plain(s) : "";

    private static ItemFacts Named(ItemFacts i, string en, string de) => i with { English = en, German = de };

    private static Dictionary<uint, string> KilnCatalogNames()
    {
        var map = new Dictionary<uint, string>();
        try
        {
            if (!Items.Available) return map;
            foreach (var i in Items.All)
                if (i.DisplayName is { Length: > 0 } d) map.TryAdd(i.Id.Value, d);
        }
        catch (Exception e) when (e is KilnUnavailableException or NotSupportedException or InvalidOperationException) { }
        return map;
    }
}
