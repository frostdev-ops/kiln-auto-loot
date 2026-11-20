// SPDX-License-Identifier: MIT
// The profiles a player's game starts with. Each is editable; "Reset" in the window puts a built-in back as it is here.
// The only lists name the game's items by their internal names (=Name), which every server knows, so they work on a
// dedicated server as in a hosted game. Their names are texts ("profile.<id>") until the player renames one. Pure.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

internal static class BuiltInProfiles
{
    public const string Everything = "everything", Wood = "wood", Stone = "stone", Plants = "plants",
        Plunder = "plunder", Treasure = "treasure";

    /// <summary>The built-in ids in the order the profile combo lists them.</summary>
    public static IReadOnlyList<string> Ids { get; } = [Everything, Wood, Stone, Plants, Plunder, Treasure];

    /// <summary>The wood logs of every region, and the twigs (from the ground and from twig bushes).</summary>
    public static IReadOnlyList<string> WoodItems { get; } =
    [
        "=Material_T1_Twigs", "=Material_T1_Wood_Logs", "=Material_T1_Wood_Logs_Dead", "=Material_T2_Wood_Hardwood",
        "=Material_T4_Wood_Logs_Palm", "=Material_T5_Wood_Logs_Pine", "=Material_T6_Wood_Logs_Bamboo",
        "=Material_T6_Wood_Logs_Tropical",
    ];

    /// <summary>Stone (the plain kind and each region's own), flint, and the ores.</summary>
    public static IReadOnlyList<string> StoneItems { get; } =
    [
        "=Build_TerrainMaterial_T1_Stone", "=Material_T1_Stone_Flint", "=Build_TerrainMaterial_T2_Stone_DeepForest",
        "=Build_TerrainMaterial_T2_Stone_Talltree", "=Build_TerrainMaterial_T3_Stone_Steppes_Limestone",
        "=Build_TerrainMaterial_T4_Sandstone", "=Build_TerrainMaterial_T5_Granite",
        "=Build_TerrainMaterial_T6_Wetlands_Stone_Limestone_Shells", "=Build_TerrainMaterial_T7_AncientLand_Stone",
        "=Build_TerrainMaterial_T7_AncientLand_Stone_Basalt",
        "=Material_T2_Ore_Copper", "=Material_T3_Ore_Tin", "=Material_T4_Ore_Iron", "=Material_T5_Silver_Ore",
        "=Material_T6_Gold_Ore", "=Material_T7_Celesthium_Ore", "=Material_T4_Sulfur", "=Material_T5_Coal",
    ];

    /// <summary>A built-in's settings. <paramref name="id"/> must be one of <see cref="Ids"/>.</summary>
    public static PersonalSettings Settings(string id)
    {
        var s = new PersonalSettings();
        switch (id)
        {
            case Everything:
                break;
            case Wood:
                s.ItemFilter = FilterMode.Only;
                s.OnlyList = string.Join(", ", WoodItems);
                s.Plants = true;                         // twig bushes
                break;
            case Stone:
                s.ItemFilter = FilterMode.Only;
                s.OnlyList = string.Join(", ", StoneItems);
                s.Stones = true;                         // stones on the ground and boulders
                s.Smash = true;
                break;
            case Plants:
                AllGroupsOff(s);
                s.Plants = s.Critters = true;
                s.Crops = s.CropsInBase = true;
                break;
            case Plunder:
                AllGroupsOff(s);
                s.Corpses = s.Bosses = s.Chests = true;
                break;
            case Treasure:
                AllGroupsOff(s);
                s.Corpses = s.Bosses = s.Chests = true;
                s.MinGearRarity = GearRarity.Rare;
                break;
            default:
                throw new ArgumentException($"no built-in profile '{id}'", nameof(id));
        }
        return s;
    }

    public static bool IsBuiltIn(string? id) => id is not null && Ids.Contains(id);

    /// <summary>Every pickup and container group off, smashing too (the profile turns on what it is about).</summary>
    private static void AllGroupsOff(PersonalSettings s)
    {
        s.GroundLoot = s.Stones = s.OldBooks = s.Collectibles = s.Plants = s.Critters = s.Crops = s.CropsInBase = s.Fish = false;
        s.Corpses = s.Bosses = s.Chests = false;
        s.Smash = s.SmashBuildables = s.FellTrees = s.SmashHives = false;
    }
}
