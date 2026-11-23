// SPDX-License-Identifier: MIT
// Everything the mod decides from the game data, in one pass over the facts: the template table, the item table, the
// quest items and the smash targets. Pure: no Kiln calls.
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Core;

/// <summary>The result of <see cref="GameRules.Classify"/>.</summary>
internal sealed record Classification(IReadOnlyList<TemplateRule> Templates, IReadOnlyList<ItemRow> Items,
    IReadOnlyList<uint> QuestItems, IReadOnlyDictionary<Guid, SmashKind> Smash)
{
    /// <summary>World loot the mod never opens or takes, whose contents count as "picked up from the world" for the
    /// salvage rules (<see cref="TemplateRules.IsWorldLootOnly"/>): locked world chests, the heaps of camps and mines.</summary>
    public IReadOnlyList<Guid> WorldLootOnly { get; init; } = [];

    /// <summary>Every template named like a fishing bait swarm (<see cref="CritterWatch.LooksLikeBait"/>), with what the
    /// game data showed of it and what the rules made of it (<see cref="TemplateRules.BaitNote"/>): the kiln.log line of a
    /// bait swarm the table lacks says why.</summary>
    public IReadOnlyDictionary<Guid, string> BaitNotes { get; init; } = new Dictionary<Guid, string>();
}

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
            GrownStages = CropRules.GrownStages(facts.Templates),
            Dropped = dropped,
            IsQuestItem = ItemRules.IsQuestItem,
            BossCorpse = TemplateRules.BossCorpses(facts.Templates),
            BuildableTwins = twins,
        };

        var rules = new List<TemplateRule>();
        var worldOnly = new List<Guid>();
        var smash = new Dictionary<Guid, SmashKind>();
        var bait = new Dictionary<Guid, string>();
        foreach (var t in facts.Templates)
        {
            var rule = TemplateRules.Classify(t, context);
            if (CritterWatch.LooksLikeBait(t.Name)) bait.TryAdd(t.Guid, TemplateRules.BaitNote(t, context, rule));
            if (rule is { } r) rules.Add(r);
            else if (TemplateRules.IsWorldLootOnly(t, context)) worldOnly.Add(t.Guid);
            if (SmashTargets.KindOf(t, buildable) is { } k) smash[t.Guid] = k;
        }

        var given = new HashSet<uint>();
        foreach (var r in rules) foreach (var id in r.Yield) given.Add(id);
        var rows = facts.Items.Select(i => ItemRules.RowOf(i, given)).ToList();
        var quest = facts.Items.Where(ItemRules.IsQuestItem).Select(i => i.Id).Distinct().ToList();
        return new Classification(rules, rows, quest, smash) { WorldLootOnly = worldOnly, BaitNotes = bait };
    }
}
