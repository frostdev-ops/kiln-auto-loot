// SPDX-License-Identifier: MIT
// Fishing bait swarms (critters) in the game's own data. Tyrion67 (Nexus 355, 0.2.7): maggots are not collected, and
// kiln.log said "critter: template Lootpickup_Scatter_VFX_Bait_T7_Maggots_Above is not in the mod's table". In the
// game data the mod reads here every bait swarm template is a critter; these tests hold that with the real templates,
// and the classification now keeps a note of what the data showed of each one, for the kiln.log line of one a
// player's table lacks.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public sealed class BaitSwarmTests
{
    // the game's bait swarm templates and their items, as Kiln Auto Loot 0.2.7 read them from the game data of
    // Kiln 0.2.0-dev.30.6 (kiln-auto-loot game-data cache, 12269 templates): components, the PickupItem and
    // DefaultInventory values, the yield
    private static readonly ItemFacts[] BaitItems =
    [
        new(700318057u, "Material_T4_DesertBug_FishingBait", new Guid("d87f6dbe-73e7-4def-bc17-5a37f2e6b0de"), "Ammunition", 250, "Scarab", "Skarabäus", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(1014899877u, "Material_T1_Fire_Fly_FishingBait", new Guid("9eeb1d48-755c-4c33-8d8c-994054e2e21e"), "Ammunition", 250, "Fire Fly", "Glühwürmchen", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(1951407227u, "Material_T5_MountainWoodlouse_FishingBait_WIP", new Guid("53be9078-0638-402a-8d1c-8cbb4355ee59"), "Ammunition", 250, "Pillbug", "Assel", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(2201448182u, "Material_T2_Moth_FishingBait", new Guid("f4421041-c0d4-4873-a901-429285c99c79"), "Ammunition", 250, "Moth", "Motte", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(2342031255u, "Material_T3_SteppesGrasshopper_FishingBait", new Guid("1f980b35-a090-4cd8-b06c-2a763b6a0b07"), "Ammunition", 250, "Grasshopper", "Heuschrecke", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(2482366505u, "Material_T7_Maggot_FishingBait", new Guid("829d518f-6412-4cab-81ee-982410a7dd13"), "Ammunition", 250, "Maggots", "Maden", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
        new(3599394660u, "Material_T6_Frogs_FishingBait", new Guid("0758643f-a0b5-4b7e-97fb-52b426c01849"), "Ammunition", 250, "Frog", "Frosch", [],
            new Dictionary<string, string> { ["ammunitionType"] = "FishingBait" }),
    ];

    private static readonly TemplateFacts[] BaitTemplates =
    [
        new(new Guid("c2091afd-f631-4160-ac79-574b951f5a4a"), "Lootpickup_Scatter_VFX_Bait_T1_Fire_Fly_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "23f52854-4c39-456e-a250-c99556b7d45a", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "9eeb1d48-755c-4c33-8d8c-994054e2e21e", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [1014899877u]),
        new(new Guid("c49b6b32-354f-455f-87e9-e8ed6ed5f107"), "Lootpickup_Scatter_VFX_Bait_T1_Fire_Fly_Below", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "72d455db-9963-431a-9252-fd068bf99f9d", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [1014899877u]),
        new(new Guid("e1d055ad-d8d0-4d2e-b919-758607e2d4ba"), "Lootpickup_Scatter_VFX_Bait_T2_Moths_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "d39bff8a-0778-4687-9245-b3ce0635abae", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [2201448182u]),
        new(new Guid("ff060e2c-f87d-4c87-a6f1-785e5ee87829"), "Lootpickup_Scatter_VFX_Bait_T3_Grasshopper_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "67303c72-b6f4-4e97-b5a9-84895a7bc622", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [2342031255u]),
        new(new Guid("8017cc3f-7da2-4809-9aed-f6fb323dfdfd"), "Lootpickup_Scatter_VFX_Bait_T4_DesertBug_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "d5be0a03-7345-4fbc-8dc6-695d246ead5d", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [700318057u]),
        new(new Guid("14aba9e2-44ac-44fe-bc5a-d55d5c289c34"), "Lootpickup_Scatter_VFX_Bait_T5_MountainWoodlice_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "b57d58d3-c832-4ab1-a018-08f2c0ac2327", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [1951407227u]),
        new(new Guid("e84e848b-7a97-4ad5-a28f-e1939a0692e8"), "Lootpickup_Scatter_VFX_Bait_T6_Frogs_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientInteractionOffer", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "InteractionOfferRuntime", "MovementColliderConfig", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "b51a63d0-586e-4160-bac3-a654abf6a970", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [3599394660u]),
        new(new Guid("2a9c39ef-14e5-45c0-9c38-d5a8cf546cc8"), "Lootpickup_Scatter_VFX_Bait_T6_Frogs_Below", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientInteractionOffer", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "InteractionOfferRuntime", "MovementColliderConfig", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource", "VfxParametersTemplateComponent"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "b51a63d0-586e-4160-bac3-a654abf6a970", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [3599394660u]),
        new(new Guid("e1d1f588-a200-4385-9eaa-bcdc152696e2"), "Lootpickup_Scatter_VFX_Bait_T7_Maggots_Above", "Prop",
            ["AreaLootOverride", "AttachToSurface", "AudioComponent", "AudioResourceComponent", "CheckPlayerInRange", "ClientExtinguish", "ClientInteractionOffer", "ClientToggleState", "ColliderResourceComponent", "CollisionFeedbackMaterialComponent", "CurrentTransform", "Debug", "DefaultInventory", "DestroyOnLoot", "DismantleOverride", "Extinguish", "InteractionOfferRuntime", "MovementColliderConfig", "NetworkToggleState", "OnDestroy", "PickupItem", "RenderTransform", "ServerTarget", "ServerToggleState", "Shelter", "SpawnTime", "StaticClientCollider", "StaticCollider", "UiOffsets", "VfxComponent", "VfxComponentResource"],
            new Dictionary<string, string> { ["DefaultInventory.defaultInventory"] = "df077be7-1bc4-47f5-ad13-4cc25823fa4c", ["DefaultInventory.keepOnRandomLoot"] = "False", ["DefaultInventory.applied"] = "False", ["DefaultInventory.type"] = "keen::ecs::DefaultInventorySetup", ["PickupItem.itemId.value"] = "0", ["PickupItem.item"] = "00000000-0000-0000-0000-000000000000", ["PickupItem.level"] = "0", ["PickupItem.rarity"] = "Common", ["PickupItem.pickupAmount"] = "0" },
            [], [2482366505u]),
    ];

    [Fact]
    public void Every_bait_swarm_template_of_the_game_is_a_critter()
    {
        var c = Facts.Run(BaitTemplates, BaitItems);
        Assert.Equal(9, BaitTemplates.Length);
        foreach (var t in BaitTemplates)
        {
            var r = Facts.Rule(c, t);
            Assert.True(r is { Group: Group.Critter, Flags: TemplateFlags.Swarm }, $"{t.Name}: {r?.Group} {r?.Flags}");
            Assert.Equal(t.Yield, r!.Yield);
        }
    }

    [Fact]
    public void The_classification_notes_what_the_data_showed_of_each_bait_template()
    {
        var c = Facts.Run(BaitTemplates, BaitItems);
        Assert.Equal(BaitTemplates.Select(t => t.Guid).Order(), c.BaitNotes.Keys.Order());
        var maggots = c.BaitNotes[new Guid("e1d1f588-a200-4385-9eaa-bcdc152696e2")];
        Assert.Contains("2482366505 Material_T7_Maggot_FishingBait (ammunitionType FishingBait)", maggots);
        Assert.Contains("taken as critter", maggots);
    }

    [Fact]
    public void A_bait_template_read_but_not_taken_says_what_the_data_showed()
    {
        var item = BaitItems.Single(i => i.Id == 2482366505u) with { Values = new Dictionary<string, string> { ["ammunitionType"] = "Invalid" } };
        var maggots = BaitTemplates.Single(t => t.Name.Contains("Maggots"));
        // new records, not "with" copies: a copy would keep the original's component set already looked up
        var noYield = new TemplateFacts(Guid.NewGuid(), maggots.Name, maggots.Family, maggots.Components, maggots.Values, [], []);
        var creature = new TemplateFacts(Guid.NewGuid(), maggots.Name, maggots.Family,
            [.. maggots.Components, "Wildlife", "Health"], maggots.Values, [], maggots.Yield);
        var c = Facts.Run([maggots, noYield, creature], [item]);
        Assert.Contains("(ammunitionType Invalid)", c.BaitNotes[maggots.Guid]);
        Assert.Contains("taken as ground", c.BaitNotes[maggots.Guid]);   // a pickup, but not bait: ground loot
        Assert.Contains("no yield", c.BaitNotes[noYield.Guid]);
        Assert.Contains("Health yes", c.BaitNotes[creature.Guid]);
        Assert.Contains("not taken", c.BaitNotes[creature.Guid]);
    }
}
