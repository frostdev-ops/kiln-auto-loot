// SPDX-License-Identifier: MIT
// The generic drops (TemplateFlags.Generic): pickups the game spawns loot as and gives their item only at the spawn
// (what broken barrels, crates and pots and some resource nodes drop). Their template says nothing about what one
// carries, so each is judged by its live item, the way the template rules judge a pickup by the item it names.
namespace KilnAutoLoot.Core;

public static class GenericDrops
{
    /// <summary>The rule a generic drop carrying <paramref name="item"/> is judged by, or null when it stays: no item
    /// yet, or a quest item that is not a collection piece (as for a quest item lying about, TemplateRules). A find kept
    /// for its own sake is a collectible, the terrain's stone is ground loot flagged stone, anything else (also an item
    /// the item table does not know) is ground loot. Its yield is the item.</summary>
    public static TemplateRule? Live(TemplateRule rule, uint item, ItemTable items, QuestItems quest)
    {
        if (item == 0) return null;
        var known = items.TryGet(item, out var row);
        var collectionPiece = known && row.Internal.StartsWith("Collection_", StringComparison.OrdinalIgnoreCase);
        if (quest.Contains(item) && !collectionPiece) return null;
        var group = known && TemplateRules.IsCollectibleItem(row.Category, row.Internal) ? Group.Collectible : Group.Ground;
        var flags = group == Group.Ground && known && TemplateRules.IsStoneName(row.Internal) ? TemplateFlags.Stone : TemplateFlags.None;
        return rule with { Group = group, Flags = flags | TemplateFlags.Generic, Yield = [item] };
    }
}
