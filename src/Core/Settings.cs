// SPDX-License-Identifier: MIT
// The settings, split by who owns them.
//   PersonalSettings: one player's choices (the player's own game, kiln-auto-loot-client.json). In a hosted game the
//     host's F8 window edits them; on a dedicated server each player's game sends its own to the server.
//   ServerSettings: what the world does for everyone (kiln-auto-loot-server.json): the mod on or off, stack size,
//     the backpack cheat, the limits a server puts on personal choices, and the personal defaults for players who
//     join without the mod.
// Property names are the settings keys (camelCase in the JSON files and on the wire). Everything applies at once
// except `enabled`, which waits for the world to start again.
using Kiln;

namespace KilnAutoLoot.Core;

/// <summary>One player's settings. Property names are the settings keys.</summary>
public sealed class PersonalSettings
{
    // Looting
    [Comment("Pick things up by walking past them. false = only what you take yourself with E.")] public bool Looting { get; set; } = true;
    [Comment("How far around you the mod reaches, in meters (also how far smashing reaches)."), Range(1, 50)] public float Radius { get; set; } = 15f;
    [Comment("Take things where they lie. false = the game's short pull range; mushrooms and other fixed pickups need to be within reach.")] public bool TakeInPlace { get; set; } = true;
    [Comment("Only take what is in plain view from where you stand.")] public bool VisibleOnly { get; set; }
    [Comment("Loose things in the open world: rocks, sticks, ore, mushrooms, dropped potions and arrows. Off: boulders are not smashed either.")] public bool GroundLoot { get; set; } = true;
    [Comment("Stones and stone piles. Also decides whether boulders and stone carts get smashed. Needs groundLoot (off: no stones, no boulders).")] public bool Stones { get; set; } = true;
    [Comment("Old books lying in villages and ruins (they give Old Books). Off by default; never in or near a base. Needs groundLoot.")] public bool OldBooks { get; set; } = false;
    [Comment("Fossils, urns, gnomes and the other finds you collect for their own sake.")] public bool Collectibles { get; set; } = true;
    [Comment("What grows on living plants: berries, herbs, fibre, reeds, fireflies.")] public bool Plants { get; set; } = true;
    [Comment("Ripe crops from farm beds, also in your base. Seedlings are left alone.")] public bool Crops { get; set; } = true;
    [Comment("Fish swimming within the radius.")] public bool Fish { get; set; }
    [Comment("Items the mod leaves where they are, comma separated. =Name = that one item, #Group = a whole group, a plain word = every item whose name contains it.")]
    public string SkipList { get; set; } = "";
    [Comment("Show a short note when an item on the skip list stays behind.")] public bool SkipNotice { get; set; } = true;

    // Which list decides, and gear rarity
    [Comment("Which list decides: skip = take everything except the skip list, only = take nothing except the only list.")]
    public FilterMode ItemFilter { get; set; } = FilterMode.Skip;
    [Comment("With itemFilter only: the items to take, the same kind of entries as the skip list.")]
    public string OnlyList { get; set; } = "";
    [Comment("Weapons, armor, rings and shields below this rarity stay where they are: common, uncommon, rare, epic or legendary.")]
    public GearRarity MinGearRarity { get; set; } = GearRarity.Common;

    // Where: a height window around you, so a cave's loot stays in the cave
    [Comment("Only take what is at most this many meters above you. 0 = no limit."), Range(0, 50)] public float HeightAbove { get; set; } = 4f;
    [Comment("Only take what is at most this many meters below you. 0 = no limit."), Range(0, 50)] public float HeightBelow { get; set; } = 4f;

    // The bag
    [Comment("Only top up stacks you already carry, up to full; never start a new stack.")] public bool FillCarriedStacks { get; set; }
    [Comment("Keep this many bag slots free: once only these are left, only stacks you carry are topped up. 0 = off."), Range(0, 64)]
    public int KeepFreeSlots { get; set; }
    [Comment("Stop taking an item once you carry this many, comma separated: =Name:500 for one item, #Group:2000 for a group.")]
    public string CarryLimits { get; set; } = "";

    // Ranges per group: 0 = use radius; a group may reach further than radius
    [Range(0, 50)] public float GroundRadius { get; set; }
    [Range(0, 50)] public float CollectiblesRadius { get; set; }
    [Range(0, 50)] public float PlantsRadius { get; set; }
    [Range(0, 50)] public float CropsRadius { get; set; }
    [Range(0, 50)] public float CorpsesRadius { get; set; }
    [Range(0, 50)] public float ChestsRadius { get; set; }

    // Enemies
    [Comment("Hold off while an enemy is within enemyDistance meters; carry on 2 seconds after the last one is gone.")] public bool EnemyPause { get; set; }
    [Range(1, 50)] public float EnemyDistance { get; set; } = 10f;

    // Containers
    [Comment("Empty the bodies of defeated enemies, and take what they drop.")] public bool Corpses { get; set; } = true;
    [Comment("Bosses, mini bosses and elites count as corpses too. false = you loot those yourself.")] public bool Bosses { get; set; } = true;
    [Comment("Seconds to wait before a newly found corpse, chest or plant is opened, so the game can fill it (0-5)."), Range(0, 5)] public float OpenDelay { get; set; } = 1f;
    [Comment("Empty chests, coffins, cupboards and heaps out in the world. Chests you built are never touched.")] public bool Chests { get; set; } = true;
    [Comment("Leave quest items in containers so you take them yourself.")] public bool LeaveQuestItems { get; set; } = true;

    // Smashing
    [Comment("Break barrels, crates, pots and wrecked furniture around you, once a second. Never in a base.")] public bool Smash { get; set; } = true;
    [Comment("Also break furniture that players could build, but only well away from any base.")] public bool SmashBuildables { get; set; } = true;
    [Comment("Experimental: bring down trees, stumps and logs around you. They may not grow back.")] public bool FellTrees { get; set; }
    [Comment("Break wild bee hives around you (some let wasps out).")] public bool SmashHives { get; set; }

    // The window
    [Comment("Language of the window and the notes: auto, en or de.")] public string Language { get; set; } = "auto";

    public PersonalSettings Clone() => (PersonalSettings)MemberwiseClone();
}

/// <summary>What the server decides for everyone.</summary>
public sealed class ServerSettings
{
    // read once when a world starts (LootServer keeps that value for the world), not marked RequiresRestart: Kiln would keep
    // the old value until the mod loads again, and the mod stays loaded from one hosted world to the next
    [Comment("Kiln Auto Loot on or off for this world. Takes effect when the world starts again.")] public bool Enabled { get; set; } = true;
    [Comment("Largest stack of a stackable item, up to 65535. 0 = the game's own sizes. If the mod is removed or this is lowered, the game cuts bigger stacks down when it loads them."), Range(0, 65535)]
    public int MaxStack { get; set; } = 9999;
    [Comment("Cheat: crafting any backpack gives the top one, each ingredient costing just one.")] public bool BackpackCheat { get; set; }
    [Comment("Write the mod's counters to its log every 5 seconds.")] public bool CounterLog { get; set; }

    [Comment("Caps on what players may choose for themselves.")] public ServerLimits Limits { get; set; } = new();
    [Comment("Settings used for players whose game does not have Kiln Auto Loot.")] public PersonalSettings Defaults { get; set; } = new();
}

/// <summary>Caps the server puts on personal settings (the server clamps what a player sends).</summary>
public sealed class ServerLimits
{
    [Comment("The furthest any player's radius may reach, in meters."), Range(1, 50)] public float MaxRadius { get; set; } = 50f;
    [Comment("false = no smashing for anyone.")] public bool AllowSmash { get; set; } = true;
    [Comment("false = no tree felling for anyone.")] public bool AllowFelling { get; set; } = true;
    [Comment("false = no bee hive smashing for anyone.")] public bool AllowHives { get; set; } = true;
}

/// <summary>Which list decides what is taken.</summary>
public enum FilterMode { Skip, Only }

/// <summary>The game's item rarities, lowest first.</summary>
public enum GearRarity { Common, Uncommon, Rare, Epic, Legendary }
