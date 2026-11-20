// SPDX-License-Identifier: MIT
// The mod's whole classification as tab-separated text, for checking it against the game (written with counterLog on).
// Sections: "template" lines (guid, group, flags, name, yield ids), "quest" lines (id, name), "smash" lines (guid,
// kind, name), "item" lines (id, internal name, menu group, offered, English, German). Pure.
using System.Text;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Core;

internal static class ClassificationDump
{
    public static string Text(GameFacts facts, Classification c)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var t in facts.Templates) names.TryAdd(t.Guid, t.Name);
        var items = new Dictionary<uint, ItemFacts>();
        foreach (var i in facts.Items) items.TryAdd(i.Id, i);
        var sb = new StringBuilder();
        sb.Append("# Kiln Auto Loot classification: ").Append(facts.Templates.Count).Append(" templates, ")
          .Append(facts.Items.Count).Append(" items read from the game\n");
        foreach (var r in c.Templates.OrderBy(r => r.Group).ThenBy(r => r.Name, StringComparer.Ordinal))
            sb.Append("template\t").Append(r.Guid).Append('\t').Append(TemplateTable.WordOf(r.Group)).Append('\t')
              .Append(Flags(r.Flags)).Append('\t').Append(r.Name).Append('\t').Append(string.Join(",", r.Yield)).Append('\n');
        foreach (var id in c.QuestItems.OrderBy(i => i))
            sb.Append("quest\t").Append(id).Append('\t').Append(items.TryGetValue(id, out var it) ? it.Name : "").Append('\n');
        foreach (var (g, k) in c.Smash.OrderBy(p => p.Value).ThenBy(p => names.GetValueOrDefault(p.Key, ""), StringComparer.Ordinal))
            sb.Append("smash\t").Append(g).Append('\t').Append(SmashTable.WordOf(k)).Append('\t').Append(names.GetValueOrDefault(g, "")).Append('\n');
        foreach (var r in c.Items)
            sb.Append("item\t").Append(r.Id).Append('\t').Append(r.Internal).Append('\t').Append(r.MenuGroup).Append('\t')
              .Append(r.Lootable ? 1 : 0).Append('\t').Append(r.English).Append('\t').Append(r.German).Append('\n');
        return sb.ToString();
    }

    public static string Flags(TemplateFlags f)
    {
        var parts = new List<string>();
        if ((f & TemplateFlags.Stone) != 0) parts.Add("stone");
        if ((f & TemplateFlags.Swarm) != 0) parts.Add("swarm");
        if ((f & TemplateFlags.Boss) != 0) parts.Add("boss");
        if ((f & TemplateFlags.Elite) != 0) parts.Add("elite");
        if ((f & TemplateFlags.Animal) != 0) parts.Add("animal");
        if ((f & TemplateFlags.Drop) != 0) parts.Add("drop");
        if ((f & TemplateFlags.Generic) != 0) parts.Add("generic");
        if ((f & TemplateFlags.Decor) != 0) parts.Add("decor");
        if ((f & TemplateFlags.Grown) != 0) parts.Add("grown");
        return string.Join(",", parts);
    }
}
