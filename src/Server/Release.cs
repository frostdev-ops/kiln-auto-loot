// SPDX-License-Identifier: MIT
// Which targets Kiln Auto Loot may take ("released"), decided from the template table (Core/TemplateRules, built from
// the player's own game data) and the entity's live state, with no data patch (Kiln dev.28 holds boot data patches on a
// hosting game, mods.boot.resource_patches):
//
//   The template table lists the templates the mod may take: loot-all targets (corpse, chest, plant incl. swarms, farm,
//   and the fog-nest resources and creature drops, whose loot-all goes out unread) and pickups (ground, collectible, plant pickups, fish). Loot-all
//   templates whose name holds "boss" are in group "boss", which LootRules always refuses (boss keys and quest
//   progress stay a hand job). Creatures that fight are group "enemy".
//
//   A target WITHOUT a pickup (container path) is released when
//     1. its template's group is corpse, chest, plant, farm or ground, or
//     2. its template's group is enemy and the creature is dead (Combat.GetHealth Dead, or the loot marker
//        DisableColliderModeChanges the game itself sets when a dead enemy turns into loot).
//   Everything else is not released: living enemies, the boss group, collectible/fish/unknown-word templates without a
//   pickup, templates missing from the table (never taken).
//
//   A pickup is released when its template is in the table with any group but enemy and boss. It only matters for
//   the accept on pickups with an inventory (mushrooms, stone piles).
//
//   Swarms (flag swarm: fireflies, moths ...) carry PickupItem in the game's data but are taken like bushes (accept,
//   then loot-all), so the mod sends them down the container path (IsContainerTarget).
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal static class Release
{
    /// <summary>The container path takes this target (no pickup, or a swarm).</summary>
    public static bool IsContainerTarget(TemplateRule rule, bool hasPickupItem) =>
        !hasPickupItem || (rule.Flags & TemplateFlags.Swarm) != 0;

    /// <summary>A target with a pickup that is taken as a pickup (not a swarm).</summary>
    public static bool IsPickupPath(TemplateRule rule) => !IsContainerTarget(rule, hasPickupItem: true);

    /// <summary>Released, for a container-path target.</summary>
    public static bool ContainerReleased(TemplateRule rule, bool dead) => rule.Group switch
    {
        Group.Corpse or Group.Chest or Group.Plant or Group.Farm or Group.Ground => true,
        Group.Enemy => dead,
        _ => false,
    };

    /// <summary>Released, for a pickup.</summary>
    public static bool PickupReleased(TemplateRule rule) => rule.Group is not (Group.Enemy or Group.Boss);

    /// <summary>A living enemy that may pause looting: group enemy, not an animal, not dead.</summary>
    public static bool IsPausingEnemy(TemplateRule rule, bool dead) =>
        rule.Group == Group.Enemy && (rule.Flags & TemplateFlags.Animal) == 0 && !dead;

    /// <summary>One harvest per visit, also without an offer. Ground loot on the container path too (the fog-nest
    /// crystals), and the piles a creature leaves (<see cref="IsDrop"/>): one loot-all per visit, also while the bag is
    /// full.</summary>
    public static bool HarvestOnce(TemplateRule rule) => rule.Group is Group.Plant or Group.Farm or Group.Ground || IsDrop(rule);

    /// <summary>A corpse group pile a creature leaves (bones, bat fur, critter parts: TemplateFlags.Drop).</summary>
    public static bool IsDrop(TemplateRule rule) => rule.Group == Group.Corpse && (rule.Flags & TemplateFlags.Drop) != 0;

    /// <summary>Targets whose loot-all is a harvest (the yield goes straight into the inventory, past the slot filter): their
    /// inventory is not read for the slot filter, and a loot-all goes out even when the inventory reads empty.</summary>
    public static bool IsHarvest(TemplateRule rule) => rule.Group is Group.Plant or Group.Farm;

    /// <summary>Ground loot on the container path whose inventory cannot be read gets its loot-all anyway, like a harvest:
    /// the fog-nest crystals (Archaic Essence) have a default inventory that is only handed over by the loot-all (the
    /// accept alone moves their offer on). The piles a creature leaves (<see cref="IsDrop"/>: hound and skeleton bones,
    /// bat fur) are built the same way. A readable one keeps the slot filter.</summary>
    public static bool LootUnread(TemplateRule rule) => rule.Group == Group.Ground || IsDrop(rule);

    /// <summary>Corpses and chests that read empty are skipped instead of sent another loot-all (they stay in the world
    /// after looting; another loot-all would move nothing). A creature's pile is not: its default inventory may not be
    /// in its inventory yet, and the loot-all hands it over (once per visit, <see cref="HarvestOnce"/>; the pile goes
    /// when it is looted).</summary>
    public static bool SkipWhenEmpty(TemplateRule rule) =>
        (rule.Group is Group.Corpse or Group.Chest or Group.Enemy) && !IsDrop(rule);
}
