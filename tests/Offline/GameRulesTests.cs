// SPDX-License-Identifier: MIT
// The rules that turn the game's data into the mod's tables (TemplateRules, CropRules, ItemRules, SmashTargets,
// GameRules), on small hand-made fixtures, and the cache and text-blob readers.
using System.Buffers.Binary;
using System.Text;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Offline;

internal static class Facts
{
    private static int _n;

    public static TemplateFacts T(string name, string[] components, Dictionary<string, string>? values = null,
        uint[]? yield = null, Guid? guid = null, Guid[]? drops = null) =>
        new(guid ?? Guid.NewGuid(), name, "", components, values ?? new(), drops ?? [], yield ?? []);

    public static ItemFacts I(uint id, string name, string category, Guid[]? templates = null, string en = "", string de = "") =>
        new(id, name, Guid.NewGuid(), category, 50, en, de, templates ?? [], new Dictionary<string, string>());

    public static Guid G() => new($"00000000-0000-0000-0000-{++_n:000000000000}");

    public static readonly string[] Pickup = ["PickupItem", "DestroyOnLoot", "DefaultInventory", "AreaLootOverride", "InteractionOfferRuntime", "ModelResource", "ModelComponent"];
    public static readonly string[] Chest = ["DefaultInventory", "InventorySetup", "InteractionOfferRuntime", "RandomLoot", "Destructible", "RandomLootContainer"];
    public static readonly string[] Bush = ["DestroyOnLoot", "DefaultInventory", "InteractionOfferRuntime", "Destructible"];
    public static readonly string[] Corpse = ["DestroyOnLoot", "DefaultInventory", "Inventory", "InventorySetup", "InteractionOfferRuntime"];
    public static readonly string[] Enemy = ["Actor", "Health", "Faction", "Enemy01BehaviorState", "HealthBar", "RandomLootContainer"];
    public static readonly string[] Crop = ["DestroyOnLoot", "DefaultInventory", "InteractionOfferRuntime", "Destructible", "UpgradeAnimation"];
    public static readonly string[] Prop = ["Destructible"];

    public static Classification Run(IEnumerable<TemplateFacts> templates, IEnumerable<ItemFacts>? items = null) =>
        GameRules.Classify(new GameFacts(templates.ToList(), (items ?? []).ToList()));

    public static TemplateRule? Rule(Classification c, TemplateFacts t) => c.Templates.FirstOrDefault(r => r.Guid == t.Guid);
}

public class TemplateRulesTests
{
    private static TemplateRule? One(TemplateFacts t, params ItemFacts[] items) => Facts.Rule(Facts.Run([t], items), t);

    [Fact]
    public void Development_templates_are_never_taken()
    {
        foreach (var n in new[] { "LootChest_Random_DEPRECATED", "Lauras_Test_Chest", "LootChest_Cheat_All", "PLACEMENT_HELPER_Goat",
                     "Z_Plant_DoNotUse", "Scatter_Loot_Coffee_Tree_Unused", "TrashHeap_bomb_DONOTPLACE", "Base_LootChest_Random" })
            Assert.Null(One(Facts.T(n, Facts.Chest)));
        // a base template the world does drop (a resource node's generic pickup) is taken
        var generic = Facts.T("Base_LootPickup_Generic_Gravity", Facts.Pickup);
        var node = Facts.T("Scatter_Loot_Tree_01", ["Destructible", "MiningNode"], drops: [generic.Guid]);
        Assert.Equal(Group.Ground, Facts.Rule(Facts.Run([generic, node]), generic)?.Group);
    }

    [Fact]
    public void A_generic_drop_without_a_pickup_in_its_template_is_ground_loot_judged_by_its_live_item()
    {
        // Base_LootPickup_Generic_Gravity as the game has it: no PickupItem and no inventory in the template (the game
        // adds the item at the spawn); what resource nodes and broken barrels and pots drop
        string[] comps = ["AttachToSurface", "CheckPlayerInRange", "ClientInteractionOffer", "DestroyOnLoot", "DoNotSave", "Gravity",
                          "InteractionOfferRuntime", "InventoryEmptyState", "PickupItemModelSetup", "ModelComponent"];
        var generic = Facts.T("Base_LootPickup_Generic_Gravity", comps);
        var node = Facts.T("Prop_Destructible_Resource_Stone_Grassland_01_b", ["Destructible", "MiningNode", "ResourceNodePickupDrops"],
            drops: [generic.Guid]);
        var rule = Facts.Rule(Facts.Run([generic, node]), generic);
        Assert.Equal(Group.Ground, rule?.Group);
        Assert.Equal(TemplateFlags.Generic, rule?.Flags);
        Assert.Empty(rule!.Yield);
        // nobody drops it: a base template, never taken; with a stock of its own it is a container, not a drop
        Assert.Null(One(Facts.T("Base_LootPickup_Generic_Gravity", comps)));
        var stocked = Facts.T("Base_LootPickup_Stocked_Gravity", [.. comps, "InventorySetup"]);
        Assert.Null(Facts.Rule(Facts.Run([stocked, Facts.T("Node", ["MiningNode"], drops: [stocked.Guid])]), stocked));
        // a dropped pickup that names its item keeps its own rule
        var stone = Facts.I(12, "Build_TerrainMaterial_T1_Stone", "Materials");
        var named = Facts.T("LootPickup_Gravity_Stone", ["PickupItem", "DestroyOnLoot", "InteractionOfferRuntime", "Gravity"], yield: [12]);
        var r2 = Facts.Rule(Facts.Run([named, Facts.T("Node2", ["MiningNode"], drops: [named.Guid])], [stone]), named);
        Assert.Equal(TemplateFlags.Stone, r2?.Flags);
    }

    [Fact]
    public void A_generic_drop_takes_its_group_from_its_live_item()
    {
        var rule = new TemplateRule(Guid.NewGuid(), Group.Ground, TemplateFlags.Generic, "Base_LootPickup_Generic_Gravity", []);
        var items = ItemTable.Parse(new StringReader(Fixtures.ItemsText +
            "9\tBuild_TerrainMaterial_T1_Stone\tStone\tStein\t1\tMaterials\tMaterials\n" +
            "10\tCollection_Test_Statue_Piece\tStatue Piece\t\t1\tCollectibles\tCollectibles\n"));
        var quest = Fixtures.Quest(4, 10);
        Assert.Null(GenericDrops.Live(rule, 0, items, quest));                     // no item yet
        Assert.Null(GenericDrops.Live(rule, 4, items, quest));                     // a quest item stays
        Assert.Equal((Group.Collectible, TemplateFlags.Generic), Of(GenericDrops.Live(rule, 10, items, quest)));
        Assert.Equal((Group.Ground, TemplateFlags.Generic | TemplateFlags.Stone), Of(GenericDrops.Live(rule, 9, items, quest)));
        Assert.Equal((Group.Ground, TemplateFlags.Generic), Of(GenericDrops.Live(rule, 7, items, quest)));   // flint: no stone
        Assert.Equal((Group.Ground, TemplateFlags.Generic), Of(GenericDrops.Live(rule, 999, items, quest))); // unknown item
        Assert.Equal([7u], GenericDrops.Live(rule, 7, items, quest)!.Yield);

        static (Group, TemplateFlags) Of(TemplateRule? r) => (r!.Group, r.Flags);
    }

    [Fact]
    public void World_chests_with_random_loot_are_chests_but_locked_and_built_ones_are_not()
    {
        Assert.Equal(Group.Chest, One(Facts.T("LootChest_Random_Cairn_Sarcophagus_Wood", Facts.Chest))?.Group);
        Assert.Null(One(Facts.T("LootChest_Locked_Dungeon", Facts.Chest)));
        var own = Facts.T("Storage_Chest_Wood", Facts.Chest);
        Assert.Null(One(own, Facts.I(1, "Build_Storage_Chest", "BuildTools", [own.Guid])));
    }

    // netjunk 2026-11-20: a Pine Shortbow from a locked world chest (lockpicks) and a Metalstar Mace from a searchable
    // junk pile in an enemy camp were left by the salvage rules as "not world loot"
    private static readonly string[] LockedChest = ["DefaultInventory", "InventorySetup", "InteractionOfferRuntime", "Destructible",
        "RandomLootSlotCount", "InteractionLock", "ClientInteractionLock"];
    private static readonly string[] LockedRandomChest = [.. LockedChest, "RandomLoot", "RandomLootContainer"];
    private static readonly string[] CampHeap = ["DefaultInventory", "InventorySetup", "InteractionOfferRuntime", "Destructible",
        "DestroyOnLoot", "RandomLootSlotCount"];

    [Fact]
    public void Locked_world_chests_and_the_heaps_of_camps_and_mines_are_world_loot_for_salvage_but_never_taken()
    {
        var world = new[]
        {
            Facts.T("LootChest_Random_Wood_Locked_Pickable_T0-T4", LockedRandomChest),
            Facts.T("LootChest_Random_gold_water_locked_T5-T6_trigger", LockedRandomChest),
            Facts.T("LootChest_Locked_Armor_lvl05_Set2_Legs", LockedChest),
            Facts.T("LootChest_Locked_Materials_Early_Game", LockedChest),
            Facts.T("Prop_Vukah_Camp_Drywood_TrashHeap01a_DONOTPLACE", CampHeap),
            Facts.T("Prop_Scavenger_Camp_TrashHeap01c_bomb_DONOTPLACE", CampHeap),
            Facts.T("Prop_Mines_oldTools_free_4m_02_earlyGame_DONOTPLACE", CampHeap),
            Facts.T("Prop_Ancients_TrashhHeap_01_b_DONOTPLACE", CampHeap),
        };
        var own = Facts.T("Prop_Decoration_T2_Storage_32_Chest", LockedChest);
        var notWorld = new[]
        {
            own,                                                                              // the player's own storage
            Facts.T("Base_LootChest_Locked_Equipment", LockedChest),                          // a base template
            Facts.T("Base_TrashHeap", CampHeap),
            Facts.T("LootChest_Random_gold_fog_trigger_locked_trigger_test", LockedRandomChest),   // development
            Facts.T("Prop_Chest01_Player_Storage_Wood_Large_DEPRECATED", LockedChest),
            Facts.T("QUEST_Model_Static_Lore_Object_Tools_searchable", ["InventorySetup", "InteractionOfferRuntime", "RandomLoot", "InteractionKnowledgeLock"]),
            Facts.T("_DONOTPLACE_Prop_Furniture_Crate_01_crafted", CampHeap),                  // a built piece
            Facts.T("Prop_Destructible_Barrel_01", Facts.Prop),                                // nothing to open
        };
        var c = Facts.Run([.. world, .. notWorld], [Facts.I(1, "Build_Storage_32", "BuildTools", [own.Guid])]);
        foreach (var t in world)
        {
            Assert.Null(Facts.Rule(c, t));                                     // auto loot never opens them
            Assert.Contains(t.Guid, c.WorldLootOnly);
        }
        foreach (var t in notWorld) Assert.DoesNotContain(t.Guid, c.WorldLootOnly);

        var table = TemplateTable.Empty();
        table.Fill(c.Templates, c.WorldLootOnly);
        Assert.True(table.IsWorldLoot(world[0].Guid));
        Assert.False(table.TryGet(world[0].Guid, out _));
        Assert.False(table.IsWorldLoot(own.Guid));
    }

    [Fact]
    public void Heaps_and_ruin_containers_are_chests()
    {
        Assert.Equal(Group.Chest, One(Facts.T("Prop_Mines_Trashheap_01", ["DestroyOnLoot", "DefaultInventory", "InventorySetup", "InteractionOfferRuntime", "Destructible"]))?.Group);
        Assert.Equal(Group.Chest, One(Facts.T("LootContainer_Prop_Shelf_01", ["DefaultInventory", "Inventory", "InventorySetup", "InteractionOfferRuntime", "Destructible"]))?.Group);
    }

    [Fact]
    public void Boss_loot_is_the_boss_group_and_corpses_carry_elite()
    {
        Assert.Equal(Group.Boss, One(Facts.T("LootPickup_Enemy_Boss_GiantLizard", Facts.Corpse))?.Group);
        var corpse = One(Facts.T("LootContainer_Enemy_Skeleton_Elite_Melee", Facts.Corpse));
        Assert.Equal(Group.Corpse, corpse?.Group);
        Assert.Equal(TemplateFlags.Elite, corpse?.Flags);
        var bones = One(Facts.T("LootPickup_Material_Bones", Facts.Corpse));
        Assert.Equal(Group.Corpse, bones?.Group);
        Assert.Equal(TemplateFlags.Drop, bones?.Flags);
        var fur = One(Facts.T("LootPickup_Material_Fur_Patch_BatLoot", ["DefaultInventory", "DestroyOnLoot", "InteractionOfferRuntime"]));
        Assert.Equal(Group.Corpse, fur?.Group);
        Assert.Equal(TemplateFlags.Drop, fur?.Flags);
        Assert.Equal(Group.Plant, One(Facts.T("LootPickup_Material_Plant_Fibre_Bush", Facts.Bush))?.Group);
    }

    [Fact]
    public void Bushes_are_plants_and_fog_nest_resources_ground()
    {
        Assert.Equal(Group.Plant, One(Facts.T("Scatter_Loot_Grassland_Berrybush_01", Facts.Bush))?.Group);
        Assert.Equal(Group.Plant, One(Facts.T("Scatter_Loot_DeepWoods_Hazelnut_d_nuts_harvest", ["DefaultInventory", "Inventory", "InventorySetup", "InteractionOfferRuntime", "Destructible"]))?.Group);
        Assert.Equal(Group.Ground, One(Facts.T("FogNest_Ressource_Wood", Facts.Bush))?.Group);
        Assert.Equal(Group.Plant, One(Facts.T("Scatter_Loot_Twig_Bush_01", Facts.Bush))?.Group);   // a bush, whatever it gives
        // an offer and a stock alone (a crafting station, a door) is nothing
        Assert.Null(One(Facts.T("T1_Forge_Factory", ["InteractionOfferRuntime", "Inventory", "InventorySetup"])));
    }

    [Fact]
    public void Story_containers_and_pickups_are_never_taken()
    {
        Assert.Null(One(Facts.T("LootPickup_NPC_Quest_Letter", Facts.Pickup)));
        Assert.Null(One(Facts.T("Prop_Lore_Chest", Facts.Chest)));
    }

    [Fact]
    public void Pickups_follow_their_item()
    {
        var fossil = Facts.I(10, "Collection_Fossil_Ammonite", "Collectibles");
        var palette = Facts.I(11, "ColorPalette_T2_03", "ColorPalette");
        var stone = Facts.I(12, "Build_TerrainMaterial_T1_Stone", "Materials");
        var flint = Facts.I(13, "Material_T1_Stone_Flint", "Materials");
        var lore = Facts.I(14, "8kLoreText_Old_Diary", "Collectibles");
        Assert.Equal(Group.Collectible, One(Facts.T("LootPickup_Collections_Fossil", Facts.Pickup, yield: [10]), fossil)?.Group);
        Assert.Equal(Group.Collectible, One(Facts.T("LootPickup_Palette", Facts.Pickup, yield: [11]), palette)?.Group);
        var s = One(Facts.T("LootPickup_Material_Stone_Grassland", Facts.Pickup, yield: [12]), stone);
        Assert.Equal((Group.Ground, TemplateFlags.Stone), (s!.Group, s.Flags));
        Assert.Equal(TemplateFlags.None, One(Facts.T("LootPickup_Material_Flint", Facts.Pickup, yield: [13]), flint)?.Flags);
        Assert.Null(One(Facts.T("LootPickup_Diary", Facts.Pickup, yield: [14]), lore));      // a quest item's pickup
        var piece = Facts.I(15, "Collection_Wetland_DrakRelief_Piece_01", "Collectibles");
        Assert.Equal(Group.Collectible, One(Facts.T("LootPickup_Collections_DrakRelief_01", Facts.Pickup, yield: [15]), piece)?.Group);
        Assert.Equal([12u], s.Yield);
    }

    [Fact]
    public void World_books_players_can_also_build_are_ground_loot_flagged_decor()
    {
        // a player's report: Kiln Auto Loot took the old books lying in their base. The world's books give Old Books
        // when taken; the ones a player builds are other templates, which are never taken at all
        var oldBooks = Facts.I(20, "Material_T1_OldBooks", "Materials", en: "Old Books");
        var worldBook = Facts.T("Prop_Furniture_Book_Pile_01_A_Lootpickup", Facts.Pickup, yield: [20]);
        var builtBook = Facts.T("_DONOTPLACE_Prop_Furniture_Book_Pile_01_A_crafted", ["Destructible", "ModelResource"]);
        var bookItem = Facts.I(21, "Prop_Decoration_Book_Pile_01_A", "BuildTools", [builtBook.Guid]);
        var twigs = Facts.T("LootPickup_Material_Twigs", Facts.Pickup, yield: [20]);
        var c = Facts.Run([worldBook, builtBook, twigs], [oldBooks, bookItem]);
        var book = Facts.Rule(c, worldBook)!;
        Assert.Equal((Group.Ground, TemplateFlags.Decor), (book.Group, book.Flags));
        Assert.Null(Facts.Rule(c, builtBook));
        Assert.Equal(TemplateFlags.None, Facts.Rule(c, twigs)!.Flags);
        // without a built piece, the same book is plain ground loot; a built piece no item places still counts
        Assert.Equal(TemplateFlags.None, Facts.Rule(Facts.Run([worldBook], [oldBooks]), worldBook)!.Flags);
        Assert.Equal(TemplateFlags.Decor, Facts.Rule(Facts.Run([worldBook, builtBook], [oldBooks]), worldBook)!.Flags);
        Assert.Equal("prop_furniture_book_pile_01_a", TemplateRules.TwinKey(worldBook.Name));
        Assert.Equal("prop_furniture_book_pile_01_a", TemplateRules.TwinKey(builtBook.Name));
    }

    [Fact]
    public void Swarms_are_critters_flagged_swarm()
    {
        var bait = Facts.I(20, "Material_T1_Fire_Fly_FishingBait", "Ammunition") with { Values = new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" } };
        var comps = new[] { "PickupItem", "DestroyOnLoot", "DefaultInventory", "InteractionOfferRuntime", "VfxComponent" };
        var r = One(Facts.T("Lootpickup_Scatter_VFX_Bait_T1_Fire_Fly", comps, yield: [20]), bait);
        Assert.Equal((Group.Critter, TemplateFlags.Swarm), (r!.Group, r.Flags));
        // an effect-only pickup of something else (flame essence) is plain ground loot
        var essence = Facts.I(21, "Material_FlameEssence", "Materials");
        var e = One(Facts.T("LootPickup_FlameEssence_T1", comps, yield: [21]), essence);
        Assert.Equal((Group.Ground, TemplateFlags.None), (e!.Group, e.Flags));
    }

    [Fact]
    public void Critters_are_never_creatures_pets_farm_animals_thrown_bait_or_quest_things()
    {
        var bait = Facts.I(20, "Material_T6_Frogs_FishingBait", "Ammunition") with { Values = new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" } };
        var swarm = new[] { "PickupItem", "DestroyOnLoot", "DefaultInventory", "InteractionOfferRuntime", "VfxComponent" };
        // a live creature with the same yield, a pet, a farm animal: never a critter (nor anything taken)
        Assert.Null(One(Facts.T("Animal_Wildlife_Frog", [.. swarm, "Actor", "Health", "Wildlife"], yield: [20]), bait));
        Assert.Null(One(Facts.T("Lootpickup_Scatter_VFX_Bait_Frog_Pet", [.. swarm, "Wildlife"], yield: [20]), bait));
        Assert.Null(One(Facts.T("Animal_Wildlife_Dog_01_tame", ["Actor", "Health", "Faction", "Wildlife", "HealthBar", "InteractionOfferRuntime"]), bait));
        Assert.Null(One(Facts.T("Animal_Baby_T2_Bunny", ["Actor", "InteractionOfferRuntime", "Faction"]), bait));
        // bait thrown for animals, and bait a quest needs
        Assert.Null(One(Facts.T("FoodBait_T6_Fish_common", ["PickupItem", "DestroyOnLoot", "FoodBait"], yield: [20]), bait));
        Assert.Null(One(Facts.T("Lootpickup_Scatter_VFX_Bait_Frogs_Quest", swarm, yield: [20]), bait));
        // a swarm with a model is something else (ground loot), not a critter
        Assert.NotEqual(Group.Critter, One(Facts.T("Lootpickup_Bait_Jar", [.. swarm, "ModelResource"], yield: [20]), bait)?.Group);
    }

    [Fact]
    public void Pickups_that_are_creatures_bait_or_displays_are_not_taken()
    {
        Assert.Null(One(Facts.T("FoodBait_T1_Meat", ["PickupItem", "DestroyOnLoot", "FoodBait"])));
        Assert.Null(One(Facts.T("Prop_Display_Sword", ["PickupItem", "InteractionOfferRuntime"])));   // no DestroyOnLoot
    }

    [Fact]
    public void Creatures_enemies_bosses_elites_animals_and_fish()
    {
        Assert.Equal(Group.Enemy, One(Facts.T("Enemy_Vukah_Melee", Facts.Enemy))?.Group);
        Assert.Equal(TemplateFlags.Boss, One(Facts.T("Enemy_Boss_Fog_Dragon", [.. Facts.Enemy, "Boss"]))?.Flags);
        Assert.Equal(TemplateFlags.Elite, One(Facts.T("Enemy_Skeleton_Elite_Melee01", Facts.Enemy))?.Flags);
        Assert.Equal(TemplateFlags.Animal, One(Facts.T("Animal_Wildlife_T1_Goat", ["Actor", "Health", "Faction", "Wildlife", "HealthBar"]))?.Flags);
        Assert.Null(One(Facts.T("ProjectileSpawner_Turret", ["Actor", "Health", "Faction", "Enemy01BehaviorState"])));   // no health bar: no creature
        var fish = One(Facts.T("Animal_Wildlife_Fish_Small01", ["Actor", "Health", "Faction", "Wildlife"], new() { ["Faction.faction"] = "Environment" }));
        Assert.Equal((Group.Fish, TemplateFlags.Animal), (fish!.Group, fish.Flags));
    }

    [Fact]
    public void Friends_villagers_and_pets_are_no_enemies()
    {
        Assert.Null(One(Facts.T("Player_Companion_Wolf", Facts.Enemy, new() { ["Faction.faction"] = "PlayerAlly" })));
        Assert.Null(One(Facts.T("NPC_Workshop_Blacksmith01", ["Actor", "Health", "Faction", "ActiveNpcState"])));
        Assert.Null(One(Facts.T("Animal_Pet_Cat_tame", ["Actor", "Health", "Faction", "Wildlife"])));
        Assert.Null(One(Facts.T("Prop_Training_Dummy", ["Actor", "Health"])));
    }

    // the components of the game's own animals, shortened to what tells them (probed in a hosted world: goats and yaks in
    // a base; the others as the mod reads the game data of 0.2.0-dev.30.6): a grown farm animal, a baby, a pet (of the
    // FarmAnimal faction too, with nothing to give), a farm bunny (no produce) and a wild goat (the farm goat's inventory
    // and refill, not tame, faction Wildlife)
    private static readonly string[] Adult =
        ["Actor", "Animal", "Faction", "Health", "HealthBar", "InteractionLootRefill", "InteractionOfferRuntime", "Inventory",
         "InventorySetup", "IsTame", "RandomLoot", "Wildlife"];
    private static readonly string[] Baby = ["Actor", "Animal", "BabyAnimal", "Faction", "InteractionOfferRuntime", "IsTame"];
    private static readonly string[] Pet = ["Actor", "Animal", "Faction", "HealthBar", "InteractionOfferRuntime", "IsTame", "Wildlife"];
    private static readonly string[] FarmBunny =
        ["Actor", "Animal", "Faction", "Health", "HealthBar", "InteractionOfferRuntime", "IsTame", "RandomLoot", "Wildlife"];
    private static readonly string[] WildGoat = [.. Adult.Except(["IsTame"])];
    private static Dictionary<string, string> Faction(string f) => new() { ["Faction.faction"] = f };

    [Fact]
    public void Grown_farm_animals_are_livestock_whatever_their_placement_helper_name()
    {
        // the eight the game has; fae deer and capybaras carry no random loot
        foreach (var n in new[] { "PLACEMENT_HELPER_T1_Animal_Farm_Goat", "PLACEMENT_HELPER_T3_Animal_Farm_Turkey",
                     "PLACEMENT_HELPER_T4_Animal_Farm_Bird_Desert_AG2", "PLACEMENT_HELPER_T4_Animal_Farm_Sanddigger",
                     "PLACEMENT_HELPER_T5_Animal_Farm_Goat_Coldheights_Variation_Mountaingoat", "PLACEMENT_HELPER_T5_Animal_Farm_Yak" })
            Assert.Equal((Group.Livestock, TemplateFlags.None), One(Facts.T(n, Adult, Faction("FarmAnimal"))) is { } r ? (r.Group, r.Flags) : default);
        foreach (var n in new[] { "PLACEMENT_HELPER_T2_Animal_Farm_Catdeer", "PLACEMENT_HELPER_T6_Animal_Farm_Capybara" })
            Assert.Equal(Group.Livestock, One(Facts.T(n, [.. Adult.Except(["RandomLoot"])], Faction("FarmAnimal")))?.Group);
    }

    [Fact]
    public void Baby_pet_bunny_wild_and_unfinished_animals_are_no_livestock()
    {
        Assert.Null(One(Facts.T("Animal_Baby_T1_Goat", Baby, Faction("PlayerAlly"))));
        Assert.Null(One(Facts.T("Animal_Baby_T1_Goat", [.. Baby, "Inventory", "InteractionLootRefill"], Faction("FarmAnimal"))));
        Assert.Null(One(Facts.T("PLACEMENT_HELPER_Animal_Pet_Dog_03_dark", Pet, Faction("FarmAnimal"))));
        Assert.Null(One(Facts.T("PLACEMENT_HELPER_T2_Animal_Farm_Bunny", FarmBunny, Faction("FarmAnimal"))));
        // a wild goat stays what it was: an animal to hunt, its remains looted once it is dead
        Assert.Equal((Group.Enemy, TemplateFlags.Animal),
            One(Facts.T("Animal_Wildlife_T1_Goat_AG2", WildGoat, Faction("Wildlife"))) is { } w ? (w.Group, w.Flags) : default);
        // any other development word keeps a farm animal out, as rule 1 does for every template
        Assert.Null(One(Facts.T("Z_PLACEMENT_HELPER_Animal_Farm_Yak_NOTFINISHED_DONOTUSE", Adult, Faction("FarmAnimal"))));
        Assert.Null(One(Facts.T("PLACEMENT_HELPER_T1_Animal_Farm_Goat_DEPRECATED", Adult, Faction("FarmAnimal"))));
        // not tame, or nothing the game refills: no livestock
        Assert.Null(One(Facts.T("PLACEMENT_HELPER_Animal_Farm_Goat_Wild", [.. Adult.Except(["IsTame"])], Faction("FarmAnimal"))));
        Assert.Null(One(Facts.T("PLACEMENT_HELPER_Animal_Farm_Goat_Empty", [.. Adult.Except(["InteractionLootRefill"])], Faction("FarmAnimal"))));
    }
}

public class CropRulesTests
{
    [Theory]
    [InlineData("T2_Plant_Growth_Beets_Seedling_2_harvest", "t2_plant_growth_beets", 2)]
    [InlineData("T1_Plant_Growth_Corn_2_harvest", "t1_plant_growth_corn", 2)]
    [InlineData("T2_Plant_Growth_BerryBush_3_harvest", "t2_plant_growth_berrybush", 3)]
    [InlineData("T1_Plant_Growth_PlantFibre_Bush_Seedling_3_harvest", "t1_plant_growth_plantfibre_bush", 3)]
    [InlineData("T6_Plant_Growth_Rice_01_d_Harvest", "t6_plant_growth_rice", 1)]
    [InlineData("T5_Plant_Growth_Tentacle_ShroudAdhesive_3_harvest_animated", "t5_plant_growth_tentacle_shroudadhesive", 3)]
    [InlineData("T3_Plant_Growth_Wheat_Seedling_0", "t3_plant_growth_wheat", 0)]
    [InlineData("_T5_Plant_Growth_Pumpkin_Seedling_1", "t5_plant_growth_pumpkin", 1)]
    [InlineData("T1_Plant_Growth_Tomato", "t1_plant_growth_tomato", -1)]
    public void Stage_from_the_name(string name, string crop, int stage) => Assert.Equal((crop, stage), CropRules.StageOf(name));

    [Fact]
    public void Only_the_last_harvest_stage_of_each_crop_is_a_farm_target()
    {
        var s0 = Facts.T("T2_Plant_Growth_Nettle_Seedling_0", Facts.Crop);
        var s1 = Facts.T("T2_Plant_Growth_Nettle_Seedling_1_harvest", Facts.Crop);
        var s2 = Facts.T("T2_Plant_Growth_Nettle_Seedling_2_harvest", Facts.Crop);
        var corn = Facts.T("T1_Plant_Growth_Corn_2_harvest", Facts.Crop);
        var cornSeed = Facts.T("T1_Plant_Growth_Corn_Seedling_1", Facts.Crop);
        // a crop the game adds later, with stages this mod has never seen
        var later = Facts.T("T9_Plant_Growth_Moonmelon_Seedling_4_harvest", Facts.Crop);
        var laterEarly = Facts.T("T9_Plant_Growth_Moonmelon_Seedling_3_harvest", Facts.Crop);
        var unused = Facts.T("Z_T5_Plant_Growth_Thing_3_harvest_UNUSED", Facts.Crop);
        var c = Facts.Run([s0, s1, s2, corn, cornSeed, later, laterEarly, unused]);
        Assert.Equal(Group.Farm, Facts.Rule(c, s2)?.Group);
        Assert.Equal(Group.Farm, Facts.Rule(c, corn)?.Group);
        Assert.Equal(Group.Farm, Facts.Rule(c, later)?.Group);
        foreach (var t in new[] { s0, s1, cornSeed, laterEarly, unused }) Assert.Null(Facts.Rule(c, t));
    }

    [Fact]
    public void World_plants_a_planted_crop_grows_into_are_flagged_grown()
    {
        // 0.2.6 (netjunk): planted algae grows into the world's own algae templates (no "Plant_Growth" in their names),
        // so they were plants and ignored "Ripe crops in a base". The plant template's stage list names them.
        var seed0 = Facts.T("T6_Plant_Growth_Algae_Seedling_0", Facts.Crop);
        var budsB = Facts.T("Scatter_Loot_Wetland_Algae_01_b_AlgaeBuds", Facts.Bush);
        var budsD = Facts.T("Scatter_Loot_Wetland_Algae_01_d_AlgaeBuds", Facts.Bush);
        var wild = Facts.T("Scatter_Loot_Wetland_Algae_01_a_Plantfibre", Facts.Bush);   // the world's, in no stage list
        var pumpkin = Facts.T("Scatter_Loot_coldheights_Pumpkin_01c", Facts.Pickup);
        var plant = Facts.T("T6_Plant_Growth_Algae", ["Growth", "Upgrade"],
            new() { ["Upgrade.stages"] = string.Join(",", seed0.Guid, budsB.Guid, budsD.Guid, pumpkin.Guid) });
        var altarL1 = Facts.T("Scatter_Loot_Altar_Ember_b", Facts.Bush);
        var altar = Facts.T("Home_Base_FlameAltar", ["Upgrade"], new() { ["Upgrade.stages"] = altarL1.Guid.ToString() });
        var c = Facts.Run([seed0, budsB, budsD, wild, pumpkin, plant, altarL1, altar]);
        Assert.Equal((Group.Plant, TemplateFlags.Grown), (Facts.Rule(c, budsB)?.Group, Facts.Rule(c, budsB)?.Flags));
        Assert.Equal((Group.Plant, TemplateFlags.Grown), (Facts.Rule(c, budsD)?.Group, Facts.Rule(c, budsD)?.Flags));
        Assert.Equal(Group.Ground, Facts.Rule(c, pumpkin)?.Group);
        Assert.True((Facts.Rule(c, pumpkin)!.Flags & TemplateFlags.Grown) != 0);
        Assert.Equal(TemplateFlags.None, Facts.Rule(c, wild)?.Flags);
        Assert.Equal(TemplateFlags.None, Facts.Rule(c, altarL1)?.Flags);   // only crops' plant templates count
        Assert.Null(Facts.Rule(c, seed0));
    }
}

public class ItemRulesTests
{
    [Theory]
    [InlineData("8kLoreText_mapPiece_x", "Collectibles", true)]
    [InlineData("8kLoreText_scroll_bard_song_01", "Collectibles", true)]
    [InlineData("Instrument_Lute_Epic", "Instrument", true)]
    [InlineData("Collection_StatuePikeMead_Piece_01", "Collectibles", true)]
    [InlineData("Collection_Worldmap_Desert", "Collectibles", true)]
    [InlineData("Prop_Component_T2_Crucible", "BuildTools", true)]
    [InlineData("Prop_NPC_Loom", "BuildTools", true)]
    [InlineData("Vanity_T2_NPC_Barber_Boots", "Equipment", true)]
    [InlineData("Prop_Production_T6_coffeemaker_brewer", "BuildTools", true)]   // an exception
    [InlineData("Collection_Fossils_01_Ammonite_01", "Collectibles", false)]      // a find for its own sake
    [InlineData("Material_T1_Wood_Logs", "Materials", false)]
    [InlineData("Weapon_T1_1H_SwordEpic_01", "Weapons", false)]
    public void Quest_items(string name, string category, bool quest) =>
        Assert.Equal(quest, ItemRules.IsQuestItem(Facts.I(1, name, category)));

    [Fact]
    public void Menu_groups_are_the_game_categories_with_rods_among_tools()
    {
        Assert.Equal("Materials", ItemRules.MenuGroupOf(Facts.I(1, "x", "Materials")));
        Assert.Equal("Tools", ItemRules.MenuGroupOf(Facts.I(1, "x", "FishingRod")));
        Assert.Equal(ItemRules.MenuGroups.Count, ItemRules.MenuGroups.Distinct().Count());
    }

    [Fact]
    public void The_window_offers_loose_items_only()
    {
        var none = new HashSet<uint>();
        Assert.True(ItemRules.IsLoose(Facts.I(1, "Material_T1_Wood_Logs", "Materials"), none));
        Assert.False(ItemRules.IsLoose(Facts.I(2, "Blueprint_Sword", "Blueprints"), none));
        Assert.False(ItemRules.IsLoose(Facts.I(3, "Hair_01", "Customization"), none));
        Assert.False(ItemRules.IsLoose(Facts.I(4, "Block_Wood", "BuildTools"), none));
        Assert.True(ItemRules.IsLoose(Facts.I(5, "Collection_paintings_01", "BuildTools"), new HashSet<uint> { 5 }));
        Assert.False(ItemRules.IsLoose(Facts.I(6, "", "Materials"), none));
    }

    [Fact]
    public void Rows_carry_names_group_and_stack()
    {
        var c = Facts.Run([], [Facts.I(7, "Material_T1_Twigs", "Materials", en: "Twigs", de: "Äste")]);
        var row = Assert.Single(c.Items);
        Assert.Equal((7u, "Material_T1_Twigs", "Twigs", "Äste", "Materials", 50), (row.Id, row.Internal, row.English, row.German, row.MenuGroup, row.OriginalMaxStack));
        Assert.Empty(c.QuestItems);
    }
}

public class SmashTargetsTests
{
    private static SmashKind? Kind(string name, string[]? comps = null, Dictionary<string, string>? values = null, bool buildable = false)
    {
        var t = Facts.T(name, comps ?? Facts.Prop, values);
        return SmashTargets.KindOf(t, buildable ? new HashSet<Guid> { t.Guid } : new HashSet<Guid>());
    }

    private static readonly Dictionary<string, string> Wood = new() { ["MiningNode.resourceType"] = "Wood" };
    private static readonly Dictionary<string, string> Stone = new() { ["MiningNode.resourceType"] = "Stone" };

    [Fact]
    public void Clutter_is_smashed()
    {
        Assert.Equal(SmashKind.Smash, Kind("Prop_Destructible_Ancients_Vase_Small_03_A"));
        Assert.Equal(SmashKind.Smash, Kind("Prop_Deco_Villageprops_Basket_02"));
        Assert.Equal(SmashKind.Smash, Kind("Props_Old_Chair_01"));
        Assert.Equal(SmashKind.Smash, Kind("Ruin_Pile_01", ["Destructible", "RandomLootOnDestroy"]));   // drops loot when broken
        Assert.Equal(SmashKind.Smash, Kind("Prop_Destructible_Deco_Skeleton_01_A"));                     // made to be broken
        Assert.Equal(SmashKind.Smash, Kind("Dummy_Prop_Furniture_Shelf_Wall_Broken_01"));                 // on a wall, still furniture
        Assert.Equal(SmashKind.Smash, Kind("Prop_Deco_Jug_Clay_01"));
        Assert.False(SmashTargets.HasClutterWord("prop_furniture_ancienttemple"));   // "furniture" holds no urn
        Assert.Null(Kind("Prop_Ancient_Cultivation_Rack_small"));        // "cultivation" holds no vat
    }

    [Fact]
    public void Needs_destructible_and_no_health_or_actor()
    {
        Assert.Null(Kind("Prop_Destructible_Vase", []));
        Assert.Null(Kind("Prop_Destructible_Vase", ["Destructible", "Health"]));
        Assert.Null(Kind("Prop_Destructible_Vase", ["Destructible", "Actor"]));
        Assert.Null(Kind("Ruin_Pile_01"));   // neither clutter nor loot
    }

    [Fact]
    public void What_stays_whole()
    {
        foreach (var n in new[] { "Prop_Destructible_Door_01", "Prop_Destructible_Fog_Barrel_01", "Prop_Destructible_Lantern",
                     "Prop_Destructible_Bed_01", "Prop_Destructible_Quest_Crate", "Prop_Destructible_Egg_Large",
                     "Prop_Destructible_Barrel_DEPRECATED", "Prop_Destructible_Wall_Shelf_Secret", "Base_Prop_Destructible_Crate" })
            Assert.Null(Kind(n));
        Assert.Null(Kind("LootContainer_Crate", ["Destructible", "RandomLootOnDestroy"]));   // the loot loop's
        Assert.Null(Kind("Prop_Crate_Pickup", ["Destructible", "PickupItem"]));
    }

    [Fact]
    public void Buildable_props_are_shared()
    {
        Assert.Equal(SmashKind.Shared, Kind("Prop_Destructible_Furniture_Barrel02_A", buildable: true));
        Assert.Equal(SmashKind.Smash, Kind("Prop_Destructible_Furniture_Barrel02_A"));
    }

    [Fact]
    public void Boulders_are_stones()
    {
        Assert.Equal(SmashKind.Stone, Kind("Prop_Destructible_Resource_Stone_Grassland_01_b", ["Destructible", "MiningNode"], Stone));
        Assert.Equal(SmashKind.StoneShared, Kind("Prop_Destructible_Resource_Stone_X", ["Destructible", "MiningNode"], Stone, buildable: true));
        // a stone-filled mine cart (clutter that drops random loot and gives stone) follows the stones switch too: seen
        // smashed with stones off in a hosted game (probe build)
        Assert.Equal(SmashKind.Stone, Kind("Prop_Ancients_Container_Mining_02_B", ["Destructible", "MiningNode", "RandomLootOnDestroy"], Stone));
        Assert.Equal(SmashKind.Smash, Kind("Prop_Ancients_Container_Mining_02_A", ["Destructible", "RandomLootOnDestroy"]));
    }

    [Fact]
    public void Wood_nodes_are_trees_but_not_harvest_trees_or_saplings()
    {
        Assert.Equal(SmashKind.Tree, Kind("Scatter_Loot_DeepForest_Below_Tree_01_b", ["Destructible", "MiningNode"], Wood));
        Assert.Null(Kind("Scatter_Loot_Mushroom_01", ["Destructible", "MiningNode"], new() { ["MiningNode.resourceType"] = "Plant" }));
        Assert.Null(Kind("Scatter_Loot_Deepforest_Hazelnut_Tree_01_Harvest", ["Destructible", "MiningNode", "DefaultInventory"], Wood));
        Assert.Null(Kind("T2_Plant_Growth_DeepWoods_Tree_01_Seedling_2", ["Destructible", "MiningNode"], Wood));
        Assert.Null(Kind("Scatter_Loot_Tree_01", ["Destructible", "MiningNode", "Health"], Wood));
        Assert.Equal(SmashKind.Tree, Kind("Scatter_Loot_DeepForest_Hazelnut_bush_b_noharvest", ["Destructible", "MiningNode"], Wood));
        Assert.Null(Kind("Loot_Node_Well_Core_Object_SkillPointPlant_01", ["Destructible", "MiningNode"], Wood));   // a skill point plant
        Assert.Null(Kind("Loot_Node_Material_Coal", ["Destructible", "MiningNode"], Stone));                       // coal, not a boulder
    }

    [Fact]
    public void Wild_hives()
    {
        Assert.Equal(SmashKind.Beehive, Kind("LootPickup_Food_Sweets_BeeHive_Wasp"));
        Assert.Equal(SmashKind.BeehiveShared, Kind("LootPickup_BeeHive_X", buildable: true));
        Assert.Null(Kind("Base_LootPickup_BeeHive_Destructible"));
        Assert.Null(Kind("LootPickup_BeeHive_X", []));
        Assert.Null(Kind("LootPickup_BeeHive_X", ["Destructible", "Health"]));
        Assert.Null(Kind("T4_Honey_Factory_Beehive"));
    }
}

public class GameDataFormatTests
{
    [Fact]
    public void The_cache_reads_back_only_for_its_own_key()
    {
        var facts = new GameFacts([Facts.T("A", ["X"], new() { ["X.y"] = "1" }, [3u], drops: [Guid.NewGuid()])], [Facts.I(3, "I", "Materials", en: "Eye", de: "Auge")]);
        var json = GameFactsJson.Write("k1", facts);
        var back = GameFactsJson.Read(json, "k1");
        Assert.NotNull(back);
        Assert.Equal(facts.Templates[0].Guid, back!.Templates[0].Guid);
        Assert.Equal("1", back.Templates[0].Value("X.y"));
        Assert.True(back.Templates[0].Has("X"));
        Assert.Equal(facts.Templates[0].Drops, back.Templates[0].Drops);
        Assert.Equal("Auge", back.Items[0].German);
        Assert.Null(GameFactsJson.Read(json, "k2"));
        Assert.Null(GameFactsJson.Read("{not json", "k1"));
        Assert.DoesNotContain('+', GameFactsJson.FileName("v1-server-0.2.0-dev.28+abc"));
    }

    [Fact]
    public void Text_blob_records_by_tag_id()
    {
        var b = new byte[8 + 2 * 24];
        BinaryPrimitives.WriteInt32LittleEndian(b, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 2);
        var tail = new List<byte>();
        foreach (var (i, id, text) in new[] { (0, 7u, "Holz"), (1, 42u, "<b>Äste</b>") })
        {
            var at = 8 + 24 * i;
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), id);
            var bytes = Encoding.UTF8.GetBytes(text);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at + 4), b.Length + tail.Count - (at + 4));
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 8), (uint)bytes.Length);
            tail.AddRange(bytes);
        }
        var table = LocaBlob.Parse([.. b, .. tail]);
        Assert.Equal("Holz", table[7]);
        Assert.Equal("Äste", LocaBlob.Plain(table[42]));
        Assert.Empty(LocaBlob.Parse(new byte[4]));
        Assert.Empty(LocaBlob.Parse(b));   // the texts lie outside: nothing read
    }

    [Fact]
    public void Tag_id_is_fnv1a_of_the_guid_bytes() =>
        Assert.Equal(0x69691905u, LocaBlob.TagOf(Guid.Empty));   // the game's id of the zero GUID

    [Fact]
    public void Tables_fill_in_place()
    {
        var items = ItemTable.Empty();
        var held = items;
        Assert.Empty(held.All);
        items.Fill([new ItemRow(1, "A", "a", "", true, "Materials", "Materials", Guid.Empty, 5)]);
        Assert.True(held.TryGet(1, out _));
        var templates = TemplateTable.Empty();
        var g = Guid.NewGuid();
        templates.Fill([new TemplateRule(g, Group.Chest, TemplateFlags.None, "C", [])]);
        Assert.True(templates.TryGet(g, out _));
        var quest = QuestItems.Empty();
        quest.Fill([5u, 5u]);
        Assert.Equal(1, quest.Count);
    }

    [Fact]
    public void The_dump_lists_every_section()
    {
        var chest = Facts.T("LootChest_Random_X", Facts.Chest);
        var vase = Facts.T("Prop_Destructible_Vase", Facts.Prop);
        var facts = new GameFacts([chest, vase], [Facts.I(9, "Instrument_Lute", "Instrument")]);
        var text = ClassificationDump.Text(facts, GameRules.Classify(facts));
        Assert.Contains($"template\t{chest.Guid}\tchest\t\tLootChest_Random_X\t", text);
        Assert.Contains("quest\t9\tInstrument_Lute", text);
        Assert.Contains($"smash\t{vase.Guid}\tsmash\tProp_Destructible_Vase", text);
        Assert.Contains("item\t9\tInstrument_Lute\tInstrument\t1", text);
    }
}

/// <summary>Not a check: with KILN_AUTOLOOT_FACTS set to a cache file the mod wrote (game-data-*.json), writes the
/// classification of that game to KILN_AUTOLOOT_DUMP, so the rules can be compared with a real game offline.</summary>
public class ClassifyCachedGame
{
    [Fact]
    public void Write_the_classification_of_a_cached_game()
    {
        var path = Environment.GetEnvironmentVariable("KILN_AUTOLOOT_FACTS");
        var output = Environment.GetEnvironmentVariable("KILN_AUTOLOOT_DUMP");
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(output)) return;
        var json = File.ReadAllText(path);
        var key = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Key").GetString()!;
        var facts = Assert.IsType<GameFacts>(GameFactsJson.Read(json, key));
        File.WriteAllText(output, ClassificationDump.Text(facts, GameRules.Classify(facts)));
    }
}
