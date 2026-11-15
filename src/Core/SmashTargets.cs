// SPDX-License-Identifier: MIT
// What the mod smashes, and as which kind, decided from the game's templates. Pure: no Kiln calls.
//
// Only things that break and stand in the way of looting are smashed: destructible world props without health of their
// own (a creature, a door with hit points and the like are never), that are clutter in ruins and camps: anything that
// rolls loot when it breaks, and breakable furniture and containers (barrels, crates, pots, baskets, shelves, chairs).
// Structure, light sources, story objects and anything a quest or a mechanism needs stay whole. The kinds:
//   Shared  a prop players can also build (an item places it): only away from build zones, so a base is never hit.
//   Stone   a rock boulder, or clutter that gives stone (a stone-filled mine cart): only while stones are collected
//           (smashing it is how its stone is gathered).
//   Tree    a tree, stump, fallen trunk or deadwood (a wood resource node in the world): only with tree felling on.
//   Beehive a wild bee hive: with hive smashing on, or felled with the trees.
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Core;

internal static class SmashTargets
{
    /// <summary>Words of breakable clutter: containers and furniture. Long words count anywhere in the name ("Barrel02",
    /// "WoodBarrel"); the short ones only at the start of a name part ("Jug_Clay", not "WoodJug"), so "furniture" is no "urn" and
    /// "cultivation" no "vat".</summary>
    public static readonly string[] Clutter =
    [
        "barrel", "crate", "basket", "pottery", "vase", "amphora", "bucket", "bottle", "chair", "stool", "bench",
        "table", "shelf", "cupboard", "cabinet", "wardrobe", "closet", "sideboard", "dresser", "drawer", "bundle", "toilet", "bathtub", "sofa",
        "box", "sack", "bag", "pot", "jar", "urn", "jug", "tub", "vat", "keg", "cart", "sink", "seat",
    ];

    /// <summary>Name parts of what stays whole even when the game lets it break: structure, ways and mechanisms, light,
    /// story and quest objects, spawners and nests, explosives, and what belongs to a player or a villager.</summary>
    public static readonly string[] Keep =
    [
        "door", "gate", "wall", "floor", "roof", "stair", "ladder", "bridge", "platform", "pillar", "column", "beam",
        "support", "fence", "palisade", "barricade", "elevator", "lift", "crane", "rope", "chain", "lever", "switch",
        "button", "trap", "secret", "hookshot", "grapple", "ballista", "balista", "flower", "skillpoint", "loot_node", "well_core",
        "lattice", "obsidian", "showcase", "meteor", "telescope", "throne", "illumination", "chandelier", "craftable", "lantern", "torch", "candle", "lamp", "brazier", "light",
        "flame", "fire", "statue", "altar", "shrine", "totem", "obelisk", "spire", "lore", "quest", "npc", "hint", "sign",
        "spawn", "nest", "egg", "enemy", "bomb", "explosive", "player", "bed", "pillow", "mirror", "curtain",
        "fog_barrel", "spore", "collect", "crafting", "station", "cozy", "nogfx", "dummygfx", "storage", "test", "debug", "deprecated", "unused", "dummy_test",
    ];

    public static SmashKind? KindOf(TemplateFacts t, ISet<Guid> buildable)
    {
        var name = t.Name.ToLowerInvariant();
        if (!t.Has("Destructible") || t.Has("Actor")) return null;
        var shared = buildable.Contains(t.Guid);
        if (IsHive(name) && !t.Has("Health")) return shared ? SmashKind.BeehiveShared : SmashKind.Beehive;
        if (t.Has("Health")) return null;
        if (IsWoodNode(t, name)) return SmashKind.Tree;
        if (IsBoulder(t, name)) return shared ? SmashKind.StoneShared : SmashKind.Stone;
        if (name.StartsWith("base_", StringComparison.Ordinal) || name.StartsWith("lootcontainer", StringComparison.Ordinal)) return null;
        if (t.Has("PickupItem") || t.Has("DefaultInventory")) return null;   // the loot loop takes it whole
        if (Any(name, Keep) && !IsWallFurniture(name)) return null;
        // the game's own word for a prop made to be broken, loot that drops when it breaks, broken furniture, clutter
        var clutter = name.StartsWith("prop_destructible_", StringComparison.Ordinal) || t.Has("RandomLootOnDestroy") ||
                      (name.Contains("furniture") && name.Contains("broken")) || HasClutterWord(name);
        if (!clutter) return null;
        // clutter that gives stone when it breaks (a stone-filled mine cart) follows the stones switch like a boulder
        if (IsStoneNode(t)) return shared ? SmashKind.StoneShared : SmashKind.Stone;
        return shared ? SmashKind.Shared : SmashKind.Smash;
    }

    /// <summary>A resource node of stone (boulders, and coal and ore for the game's tools).</summary>
    private static bool IsStoneNode(TemplateFacts t) =>
        t.Has("MiningNode") && string.Equals(t.Value("MiningNode.resourceType"), "Stone", StringComparison.OrdinalIgnoreCase);

    public static bool HasClutterWord(string lowerName)
    {
        var parts = lowerName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in Clutter)
        {
            if (w.Length >= 5 ? lowerName.Contains(w, StringComparison.Ordinal) : parts.Any(p => p.StartsWith(w, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    /// <summary>Furniture that hangs on a wall (a wall shelf, broken wall furniture): clutter, not the wall.</summary>
    private static bool IsWallFurniture(string n) =>
        n.Contains("wall") && (n.Contains("shelf") || n.Contains("furniture")) &&
        !Any(n.Replace("wall", ""), Keep);

    /// <summary>A wild bee hive (the ones in trees and rocks, with honey).</summary>
    public static bool IsHive(string lowerName) =>
        lowerName.Contains("beehive") && !lowerName.StartsWith("base_", StringComparison.Ordinal) && !Any(lowerName, ["factory", "craft", "deprecated", "test"]);

    /// <summary>A wood resource node out in the world (not a planted tree, not one that gives a harvest).</summary>
    public static bool IsWoodNode(TemplateFacts t, string lowerName) =>
        t.Has("MiningNode") && string.Equals(t.Value("MiningNode.resourceType"), "Wood", StringComparison.OrdinalIgnoreCase) &&
        !lowerName.Contains("_harvest") && !lowerName.Contains("plant_growth") && !t.Has("DefaultInventory") &&
        !lowerName.StartsWith("base_", StringComparison.Ordinal) && !Any(lowerName, ["deprecated", "unused", "test", "loot_node", "skillpoint", "container"]);

    /// <summary>A rock boulder: a stone resource node whose name says it is rock (coal and ore nodes also count as stone
    /// for the game's tools, but are not boulders).</summary>
    public static bool IsBoulder(TemplateFacts t, string lowerName) =>
        t.Has("MiningNode") && string.Equals(t.Value("MiningNode.resourceType"), "Stone", StringComparison.OrdinalIgnoreCase) &&
        Any(lowerName, ["stone", "rock", "boulder"]) && !lowerName.StartsWith("base_", StringComparison.Ordinal) &&
        !Any(lowerName, ["deprecated", "unused", "test"]);

    private static bool Any(string s, string[] words)
    {
        foreach (var w in words) if (s.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }
}
