// SPDX-License-Identifier: MIT
// Which of the game's templates the mod may take, and as what (the loot group the switches and radii work on), decided
// from what a template is made of: its components, a few of their values, the items it gives and, where the game data
// says nothing better, words in its name. Pure: fed GameFacts records, no Kiln calls. A template no rule takes is not in
// the table, and a template that is not in the table is never taken.
//
// In order:
//   0. Grown farm animals (livestock: the goats, yaks, ... a player keeps in a base) are taken from, although the game
//      names them "PLACEMENT_HELPER_..._Animal_Farm_..." (a development word for rule 1): their produce (milk, wool)
//      waits in their own inventory, refilled by the game. Told by their components: an animal, tame, of the
//      FarmAnimal faction, with an inventory the game refills. Babies (BabyAnimal, PlayerAlly, no inventory), pets (no
//      inventory), farm bunnies (no produce) and wild animals (not tame) are not livestock, and neither is an animal
//      with any other development word in its name (an unfinished "..._NOTFINISHED_DONOTUSE" yak).
//   1. Work-in-progress and developer templates (deprecated, test, debug, placement helpers, cheats, unused) are never taken,
//      and neither are base templates, except the generic drops resource nodes spawn and a confirmed live Vukah Ranger.
//   2. Creatures (Actor + Health) are enemies (pause and, once dead, their corpse) or fish; villagers, summons, pets and
//      farm animals are nothing. A fish is one swimming free that a player grabs by hand: built as the game's pickup
//      (IsFreeFish). A fish a player let go (tame) stays free; a fish on the hook and a fishing spot are no creature
//      with a pickup at all: their fish is an item only once the rod catches it.
//   3. Crops (plant growth stages): only the last stage of a crop that can be harvested (farm). Earlier stages may be
//      harvestable too, but harvesting them early would waste the crop. The world's plants a crop's stage list names
//      (planted algae, wheat and pumpkins grow into them) are taken as what they are (rules 4 and 5), flagged grown:
//      a crop while the player is in or near a base.
//   4. Pickups (PickupItem): never quest things; collectibles by their item's category; critters (the fishing bait
//      swarms: fireflies, moths, frogs, maggots ..., an effect with no model) are their own group; the rest is ground
//      loot, stones flagged, and so is furniture the world places that players can also build (the books of villages
//      and ruins: decor, the oldBooks switch). The generic drops (what other templates spawn their loot as, with no item of their own: the game
//      sets it at the spawn) are ground loot flagged generic, judged by their live item (GenericDrops), also the ones
//      that carry no PickupItem in their template.
//   5. Things the player empties or harvests (an offer and a default inventory): boss loot is never taken; enemy remains
//      are corpses; world chests and heaps are chests (the player's own storage, locked chests and the
//      ones an item places are not); bushes (twig bushes too), reeds and harvest trees are plants; fog-nest resources are ground.
namespace KilnAutoLoot.Core;

public static class TemplateRules
{
    /// <summary>Factions whose creatures never count as enemies: the player's side, farm animals, the world itself.</summary>
    private static readonly HashSet<string> FriendlyFactions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Player", "PlayerAlly", "AutomatedPlayer", "FarmAnimal", "Environment",
    };

    /// <summary>Name words of templates that exist for development only.</summary>
    private static readonly string[] DevelopmentWords =
    [
        "deprecated", "depricated", "_test", "test_", "debug", "placement_helper", "donotuse", "donotplace", "notfinished", "cheat",
        "unused", "_wip",
    ];

    /// <summary>Name words of templates that belong to a quest, a villager or the story: taking them is the player's own step.</summary>
    private static readonly string[] StoryWords = ["quest", "npc_", "_npc", "townsfolk", "lore", "mappiece", "_hint"];

    /// <summary>The context the rules need beyond one template.</summary>
    public sealed class Context
    {
        public required IReadOnlyDictionary<uint, ItemFacts> Items { get; init; }
        /// <summary>Templates some item places or refers to (players build them).</summary>
        public required ISet<Guid> Buildable { get; init; }
        /// <summary>The last harvestable growth stage of each crop.</summary>
        public required ISet<Guid> RipeCrops { get; init; }
        /// <summary>World plants a planted crop grows into (<see cref="CropRules.GrownStages"/>): flagged grown.</summary>
        public ISet<Guid> GrownStages { get; init; } = new HashSet<Guid>();
        /// <summary>Templates other templates drop as pickups (mined resources falling to the ground).</summary>
        public required ISet<Guid> Dropped { get; init; }
        public required Func<ItemFacts, bool> IsQuestItem { get; init; }
        /// <summary>Whether the enemy a corpse belongs to is a boss or mini boss (see <see cref="BossCorpses"/>).</summary>
        public Func<string, bool> BossCorpse { get; init; } = _ => false;
        /// <summary>The <see cref="TwinKey"/> of every template some item places or the game keeps as a built piece
        /// (<see cref="IsBuiltPiece"/>): a pickup with the same key is the world's copy of furniture (decor).</summary>
        public ISet<string> BuildableTwins { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>A template the game keeps as the built form of a piece ("_DONOTPLACE_..._crafted"), whether or not
    /// an item places it today.</summary>
    public static bool IsBuiltPiece(string name)
    {
        var lower = name.ToLowerInvariant().TrimStart('_');
        return lower.StartsWith("donotplace_", StringComparison.Ordinal) &&
               lower.EndsWith("_crafted", StringComparison.Ordinal);
    }

    /// <summary>A template's name without the words that tell the world's copy of a piece from the one an item places:
    /// lower case, without leading underscores, "donotplace_", "_crafted" and "_lootpickup". The world's
    /// "Prop_Furniture_Book_Pile_01_A_Lootpickup" and the built "_DONOTPLACE_Prop_Furniture_Book_Pile_01_A_crafted"
    /// share "prop_furniture_book_pile_01_a".</summary>
    public static string TwinKey(string name)
    {
        var key = name.ToLowerInvariant().TrimStart('_');
        if (key.StartsWith("donotplace_", StringComparison.Ordinal)) key = key["donotplace_".Length..];
        foreach (var end in new[] { "_crafted", "_lootpickup" })
            if (key.EndsWith(end, StringComparison.Ordinal)) key = key[..^end.Length];
        return key;
    }

    /// <summary>A base template ("Base_...", "_Base_..."): the others copy it, the world does not place it.</summary>
    public static bool IsBase(string lowerName) => lowerName.TrimStart('_').StartsWith("base_", StringComparison.Ordinal);

    /// <summary>World loot the mod never opens or takes (no rule above takes it), whose contents still come from the world
    /// for the salvage rules: locked world chests (a key or lockpicks, the player's own step) and the heaps of camps and
    /// mines the world places under "_DONOTPLACE" names (scavenger and Vukah trash heaps, old tools, the ancients' trash
    /// heaps), which rule 1 leaves as development names. Never the player's own storage, a built piece, a base template,
    /// a quest object or another development template.</summary>
    public static bool IsWorldLootOnly(TemplateFacts t, Context c)
    {
        var name = t.Name.ToLowerInvariant();
        if (!t.Has("InteractionOfferRuntime") || !t.Has("InventorySetup") || t.Has("Actor") || t.Has("PickupItem")) return false;
        if (IsBase(name) || IsBuiltPiece(t.Name) || c.Buildable.Contains(t.Guid) || AnyWord(name, StoryWords)) return false;
        if (AnyWord(name.Replace("donotplace", "", StringComparison.Ordinal), DevelopmentWords)) return false;
        // locked world chests: random loot, or a set of loot slots
        if (name.Contains("locked") && (t.Has("RandomLoot") || t.Has("RandomLootSlotCount"))) return true;
        // heaps searched once, then gone (the world chest rule's heaps)
        return t.Has("DefaultInventory") && t.Has("DestroyOnLoot") && t.Has("Destructible");
    }

    /// <summary>Not a development template (rule 1).</summary>
    public static bool IsWorkable(string name) => !AnyWord(name.ToLowerInvariant(), DevelopmentWords);

    /// <summary>The rule for one template, or null when the mod leaves it alone. A plant or ground template a planted crop
    /// grows into is flagged grown: in a base it counts as a crop (LootRules.Decide).</summary>
    public static TemplateRule? Classify(TemplateFacts t, Context c)
    {
        var r = RuleOf(t, c);
        return r is { Group: Group.Plant or Group.Ground } && c.GrownStages.Contains(t.Guid)
            ? r with { Flags = r.Flags | TemplateFlags.Grown }
            : r;
    }

    private static TemplateRule? RuleOf(TemplateFacts t, Context c)
    {
        if (IsLivestock(t)) return Rule(t, Group.Livestock, TemplateFlags.None);
        var name = t.Name.ToLowerInvariant();
        if (!IsWorkable(name)) return null;
        if (IsBase(name) && !c.Dropped.Contains(t.Guid))
        {
            // Normally a base is only inherited. The game actually spawns this Ranger under its base template, and
            // on death it adds a readable loot inventory. Keep every other base on the old path. The normal enemy
            // classification and live corpse gates still apply; no fixed yield is needed for its readable slots.
            if (name == "base_enemy_vukah_ranger" && t.Has("Actor") && t.Has("Health") && t.Has("RandomLoot") &&
                string.Equals(t.Value("Faction.faction"), "Vukah", StringComparison.OrdinalIgnoreCase))
                return Creature(t, name);
            return null;
        }
        if (t.Has("Actor") && t.Has("Health")) return Creature(t, name);
        if (CropRules.IsGrowthStage(t)) return c.RipeCrops.Contains(t.Guid) ? Rule(t, Group.Farm, TemplateFlags.None) : null;
        if (t.Has("PickupItem")) return Pickup(t, name, c);
        if (c.Dropped.Contains(t.Guid) && IsGenericDrop(t)) return Rule(t, Group.Ground, TemplateFlags.Generic);
        if (t.Has("InteractionOfferRuntime") && (t.Has("DefaultInventory") || t.Has("RandomLoot"))) return Container(t, name, c);
        return null;
    }

    // ---------------------------------------------------------------- creatures

    /// <summary>A grown farm animal with produce to collect (rule 0): an actor and an animal, tame, not a baby, of the
    /// FarmAnimal faction, with an inventory the game refills (InteractionLootRefill), and no development word in its
    /// name but the "placement_helper" every farm animal has. Its produce is taken from that inventory, never with the
    /// animal's E offer (which upgrades a baby and pets an animal with nothing to give).</summary>
    public static bool IsLivestock(TemplateFacts t) =>
        t.Has("Actor") && t.Has("Animal") && t.Has("IsTame") && !t.Has("BabyAnimal") &&
        t.Has("Inventory") && t.Has("InteractionLootRefill") &&
        string.Equals(t.Value("Faction.faction"), "FarmAnimal", StringComparison.OrdinalIgnoreCase) &&
        IsWorkable(t.Name.ToLowerInvariant().Replace("placement_helper", "", StringComparison.Ordinal));

    /// <summary>A fish swimming free, as the game lets a swimming player grab it with E: a wildlife creature built as
    /// a pickup, with the components the game's own interaction pickup (loot_pickup_item) works on
    /// (InteractionOfferRuntime, PickupItem, DestroyOnLoot) and the item it gives in its PickupItem. In the game data
    /// of 0.2.0-dev.30.6: Shimmerfin and Lakehopper (Animal_wildlife_Fish_T1_common_01/02) and the Fish Cutlet fish
    /// (Animal_Wildlife_Fish_Big/Medium/Small), whose spawners are nearly all in the Veilwater Basin's lakes. Not a
    /// tame one: a raw fish's Release (UseItem_Drop_Fish_Into_World) lets that fish go into a pond as "..._tame", and
    /// it stays there as a pet does. The fish a fishing spot shows and the one on the hook are no such creature: the
    /// spot's effect draws them, the rod's minigame makes the fish (Fishable, no pickup) from the spot's fish table
    /// when one bites, and its item exists only once caught.</summary>
    public static bool IsFreeFish(TemplateFacts t, string lowerName) =>
        t.Has("PickupItem") && t.Has("InteractionOfferRuntime") && t.Has("DestroyOnLoot") &&
        !t.Has("IsTame") && !lowerName.Contains("_tame");

    private static TemplateRule? Creature(TemplateFacts t, string name)
    {
        if (t.Has("ActiveNpcState") || name.StartsWith("npc_", StringComparison.Ordinal) || name.Contains("townsfolk")) return null;
        if (t.Has("Wildlife") && name.Contains("fish"))
            return IsFreeFish(t, name) ? Rule(t, Group.Fish, TemplateFlags.Animal) : null;
        if (t.Value("Faction.faction") is { } faction && FriendlyFactions.Contains(faction)) return null;
        if (name.Contains("_tame")) return null;   // pets
        if (!t.Has("Faction") || !t.Has("HealthBar")) return null;   // turrets and spawners that only look like creatures
        if (!t.Has("Enemy01BehaviorState") && !t.Has("Wildlife")) return null;
        var flags = TemplateFlags.None;
        if (t.Has("Boss")) flags |= TemplateFlags.Boss;
        if (name.Contains("elite")) flags |= TemplateFlags.Elite;
        if (t.Has("Wildlife")) flags |= TemplateFlags.Animal;
        return Rule(t, Group.Enemy, flags);
    }

    // ---------------------------------------------------------------- pickups

    private static TemplateRule? Pickup(TemplateFacts t, string name, Context c)
    {
        if (t.Has("Wildlife") || t.Has("FoodBait") || t.Has("Actor")) return null;            // creatures, thrown bait
        if (!t.Has("DestroyOnLoot")) return null;                                             // stays where it is: a display, not loot
        if (t.Has("InventorySetup") && !t.Has("DefaultInventory")) return null;               // a dropped bag or tool box: the player's own
        var item = t.Yield.Count > 0 && c.Items.TryGetValue(t.Yield[0], out var it) ? it : null;
        // a quest item lying about is left for the player, except collection pieces: picking those up is the game's own way
        if ((item is not null && c.IsQuestItem(item) && !IsCollectionPiece(item)) || AnyWord(name, StoryWords)) return null;
        if (item is not null && IsCollectibleItem(item.Category, item.Name))
            return Rule(t, Group.Collectible, TemplateFlags.None);   // finds kept for their own sake (paintings are building pieces)
        if (IsSwarm(t, item)) return Rule(t, Group.Critter, TemplateFlags.Swarm);
        if (t.Has("Health") && !IsStone(item)) return null;                                    // something to fight or break first
        if (item is null && c.Dropped.Contains(t.Guid)) return Rule(t, Group.Ground, TemplateFlags.Generic);   // its item comes at the spawn
        var flags = IsStone(item) ? TemplateFlags.Stone : TemplateFlags.None;
        // furniture the world places that players can also build: in a base it may be part of the décor
        if (!c.Buildable.Contains(t.Guid) && c.BuildableTwins.Contains(TwinKey(t.Name))) flags |= TemplateFlags.Decor;
        return Rule(t, Group.Ground, flags);
    }

    /// <summary>A template other templates spawn their loot as (<see cref="Context.Dropped"/>: a resource node's
    /// pickupItemReference, a loot slot's genericPickupItem) that is taken like a pickup but names no item: the game
    /// adds the item when it spawns one. Not a creature or bait, and nothing with a stock of its own.</summary>
    private static bool IsGenericDrop(TemplateFacts t) =>
        t.Has("DestroyOnLoot") && t.Has("InteractionOfferRuntime") &&
        !t.Has("Health") && !t.Has("Actor") && !t.Has("Wildlife") && !t.Has("FoodBait") &&
        !t.Has("Inventory") && !t.Has("InventorySetup") && !t.Has("DefaultInventory");

    private static bool IsCollectionPiece(ItemFacts item) => item.Name.StartsWith("Collection_", StringComparison.OrdinalIgnoreCase);

    /// <summary>A swarm (fireflies, moths, grasshoppers, scarabs, pillbugs, frogs, maggots): living fishing bait, shown as
    /// an effect with no model, with a stock to take. Built like a mushroom (a pickup with a default inventory and the
    /// game's pickup interaction), but its pickup names no item: the bait is its default inventory. Never a creature (no
    /// Actor, Wildlife or FoodBait: rule 2 and the pickup checks before this one).</summary>
    private static bool IsSwarm(TemplateFacts t, ItemFacts? item) =>
        t.Has("DefaultInventory") && !t.Has("ModelResource") && !t.Has("ModelComponent") &&
        item?.Value("ammunitionType") == "FishingBait";

    /// <summary>What the game data shows of a template named like a bait swarm, for the kiln.log line of one the table
    /// lacks (Tyrion67, 0.2.7: "T7_Maggots_Above is not in the mod's table"): its yield with each item's ammunitionType,
    /// the components the swarm rule reads (<see cref="IsSwarm"/>), and what <paramref name="rule"/> made of it.</summary>
    public static string BaitNote(TemplateFacts t, Context c, TemplateRule? rule)
    {
        var yield = t.Yield.Count == 0
            ? "no yield (its pickup names no item and its stock gave none)"
            : "yield " + string.Join(", ", t.Yield.Select(id => c.Items.TryGetValue(id, out var i)
                ? $"{id} {i.Name} (ammunitionType {i.Value("ammunitionType") ?? "none"})"
                : $"{id} (not in the item data)"));
        string Has(string component) => t.Has(component) ? "yes" : "no";
        var model = t.Has("ModelResource") || t.Has("ModelComponent") ? "yes" : "no";
        var taken = rule is null ? "not taken" : $"taken as {TemplateTable.WordOf(rule.Group)}";
        return $"{yield}; PickupItem {Has("PickupItem")}, DefaultInventory {Has("DefaultInventory")}, model {model}, " +
               $"DestroyOnLoot {Has("DestroyOnLoot")}, Health {Has("Health")}; {taken}";
    }

    /// <summary>Rock of the terrain (the stone, sandstone, limestone or granite each region is made of), as the game's
    /// terrain materials name it; not flint, ore, sand or dirt.</summary>
    public static bool IsStone(ItemFacts? item) => item is not null && IsStoneName(item.Name);

    /// <summary><see cref="IsStone"/> by the item's internal name.</summary>
    public static bool IsStoneName(string internalName) =>
        internalName.StartsWith("Build_TerrainMaterial_", StringComparison.OrdinalIgnoreCase) &&
        RockWords.Any(w => internalName.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>A find kept for its own sake (collectible group), by the item's category and internal name.</summary>
    public static bool IsCollectibleItem(string category, string internalName) =>
        category is "Collectibles" or "ColorPalette" || internalName.StartsWith("Collection_", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] RockWords = ["stone", "granite", "slate", "basalt", "marble", "rock"];

    // ---------------------------------------------------------------- containers and harvests

    private static TemplateRule? Container(TemplateFacts t, string name, Context c)
    {
        if (AnyWord(name, StoryWords)) return null;
        if (name.Contains("boss")) return Rule(t, Group.Boss, TemplateFlags.None);           // boss loot: always by hand
        if (c.Buildable.Contains(t.Guid)) return null;                                        // the player's own (storage, stations)
        var flags = (name.Contains("elite") ? TemplateFlags.Elite : TemplateFlags.None);
        // enemy remains: what an enemy leaves behind, and the critter parts and bones that drop as containers
        if (name.Contains("lootcontainer_enemy"))
        {
            if (c.BossCorpse(name)) flags |= TemplateFlags.Boss;
            return Rule(t, Group.Corpse, flags);
        }
        // material a creature drops as a container (bat fur, bones, critter parts); growing material is a plant (below).
        // Most of them have no inventory of their own (DefaultInventory only, like the fog-nest crystals): the loot-all
        // hands it over, so they are flagged drop (Release.LootUnread)
        if (name.StartsWith("lootpickup_material", StringComparison.Ordinal) && !t.Has("Destructible"))
            return Rule(t, Group.Corpse, flags | TemplateFlags.Drop);
        if (name.Contains("locked")) return null;                                              // opened with a key, by the player
        // trees that give nuts or beans and stay standing (harvested in place, refilled later)
        if (name.Contains("_harvest") && !t.Has("RandomLoot") && t.Has("DefaultInventory")) return Rule(t, Group.Plant, TemplateFlags.None);
        // world chests: random loot in a container with an inventory
        if (t.Has("RandomLoot") && t.Has("InventorySetup")) return Rule(t, Group.Chest, TemplateFlags.None);
        // containers in ruins (crates, shelves, cupboards) and heaps (trash, deadwood, tools) that keep a stock
        if (t.Has("DefaultInventory") && t.Has("InventorySetup") && (t.Has("Inventory") || t.Has("DestroyOnLoot")) && t.Has("Destructible"))
            return Rule(t, Group.Chest, TemplateFlags.None);
        if (!t.Has("DefaultInventory")) return null;
        // harvested in place: bushes, reeds, straw, plants with a stock; trees that give nuts or beans and stay
        if (t.Has("DestroyOnLoot") || t.Has("UpgradeAnimation"))
        {
            if (name.Contains("fognest")) return Rule(t, Group.Ground, TemplateFlags.None);   // what a fog nest leaves: loose material
            if (t.Has("Destructible") || t.Has("UpgradeAnimation")) return Rule(t, Group.Plant, TemplateFlags.None);
        }
        return null;
    }

    /// <summary>Corpses whose name does not follow their enemy's (lower case, after "lootcontainer_enemy_" and after
    /// "enemy_"). The game data has no link from an enemy to its corpse template (the enemy names neither the corpse
    /// nor its loot), so these two are paired by hand: the heavy berserker and the heavy mage of the fog camps.</summary>
    private static readonly Dictionary<string, string> CorpseOfEnemy = new(StringComparer.Ordinal)
    {
        ["fogger_berserker"] = "fogger_heavy_berserker",
        ["fogger_heavymage"] = "fogger_mageheavy",
    };

    /// <summary>A corpse "LootContainer_Enemy_X" belongs to the enemy "Enemy_X", or, when there is none, to the enemies
    /// whose names go on from "Enemy_X_"; it is a boss corpse when that enemy (or one of those variants) is a boss (the game's Boss component: bosses
    /// and the mini bosses of camps and dungeons). X may be renamed first (<see cref="CorpseOfEnemy"/>). Returns the
    /// test for lower-case corpse names.</summary>
    public static Func<string, bool> BossCorpses(IEnumerable<TemplateFacts> templates)
    {
        var enemies = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var t in templates)
            if (t.Has("Actor") && t.Has("Health") && t.Name.StartsWith("Enemy_", StringComparison.OrdinalIgnoreCase))
                enemies[t.Name.ToLowerInvariant()["enemy_".Length..]] = t.Has("Boss") || (enemies.TryGetValue(t.Name.ToLowerInvariant()["enemy_".Length..], out var b) && b);
        return corpse =>
        {
            var i = corpse.IndexOf("lootcontainer_enemy_", StringComparison.Ordinal);
            if (i < 0) return false;
            var x = corpse[(i + "lootcontainer_enemy_".Length)..];
            if (CorpseOfEnemy.TryGetValue(x, out var renamed)) x = renamed;
            if (enemies.TryGetValue(x, out var exact)) return exact;
            return enemies.Any(e => e.Value && e.Key.StartsWith(x + "_", StringComparison.Ordinal));
        };
    }

    private static TemplateRule Rule(TemplateFacts t, Group g, TemplateFlags f) => new(t.Guid, g, f, t.Name, t.Yield);

    private static bool AnyWord(string lowerName, string[] words)
    {
        foreach (var w in words) if (lowerName.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }
}
