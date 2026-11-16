// SPDX-License-Identifier: MIT
// Everything the mod decides from the game data, in one pass over the facts: the template table, the item table, the
// quest items and the smash targets. Pure: no Kiln calls.
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Core;

/// <summary>The result of <see cref="GameRules.Classify"/>.</summary>
internal sealed record Classification(IReadOnlyList<TemplateRule> Templates, IReadOnlyList<ItemRow> Items,
    IReadOnlyList<uint> QuestItems, IReadOnlyDictionary<Guid, SmashKind> Smash);

internal static class GameRules
{
    public static Classification Classify(GameFacts facts)
    {
        var items = new Dictionary<uint, ItemFacts>();
        foreach (var i in facts.Items) items.TryAdd(i.Id, i);
        var buildable = new HashSet<Guid>();
        foreach (var i in facts.Items) foreach (var g in i.Templates) buildable.Add(g);
        var dropped = new HashSet<Guid>();
        foreach (var t in facts.Templates) foreach (var g in t.Drops) dropped.Add(g);

        var twins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in facts.Templates)
            if (buildable.Contains(t.Guid) || TemplateRules.IsBuiltPiece(t.Name))
                twins.Add(TemplateRules.TwinKey(t.Name));

        bool Usable(TemplateFacts t) => TemplateRules.IsWorkable(t.Name);
        var context = new TemplateRules.Context
        {
            Items = items,
            Buildable = buildable,
            RipeCrops = CropRules.RipeStages(facts.Templates, Usable),
            Dropped = dropped,
            IsQuestItem = ItemRules.IsQuestItem,
            BossCorpse = TemplateRules.BossCorpses(facts.Templates),
            BuildableTwins = twins,
        };

        var rules = new List<TemplateRule>();
        var smash = new Dictionary<Guid, SmashKind>();
        foreach (var t in facts.Templates)
        {
            if (TemplateRules.Classify(t, context) is { } r) rules.Add(r);
            if (SmashTargets.KindOf(t, buildable) is { } k) smash[t.Guid] = k;
        }

        var given = new HashSet<uint>();
        foreach (var r in rules) foreach (var id in r.Yield) given.Add(id);
        var rows = facts.Items.Select(i => ItemRules.RowOf(i, given)).ToList();
        var quest = facts.Items.Where(ItemRules.IsQuestItem).Select(i => i.Id).Distinct().ToList();
        return new Classification(rules, rows, quest, smash);
    }
}
