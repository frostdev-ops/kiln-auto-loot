// SPDX-License-Identifier: MIT
// The pure rules of Kiln Auto Loot (no Kiln calls, so tests/Offline covers them). The server part and the
// player's window both call them.
//
// The shared types live here; each class has its own file in src/Core/:
//   TemplateTable.cs, ItemTable.cs, QuestItems.cs   the tables built from the game data (GameRules.cs and the *Rules.cs)
//   IgnoreList.cs                                   skipList: matching, unknown entries, the F8 editor
//   LootRules.cs                                    group allowed, radii, the slot filter
//   VisitTable.cs                                   the container visit memory
//   PickupTracker.cs, FullTracker.cs                pickups, "inventory full"
//   NoticeBook.cs, EnemyPause.cs                    "left behind" limits, enemy pause
//   KeyNames.cs                                     the default keys
//   Texts.cs                                        every window and notice text, English and German
//   TextRules.cs                                    trim, number parsing and ASCII compare, shared by the above
namespace KilnAutoLoot.Core;

/// <summary>A template's loot group (TemplateRules). <see cref="Unknown"/>: a group word the fixtures do not know.
/// <see cref="Livestock"/>: a grown farm animal whose produce (milk, wool, ...) waits in its own inventory.</summary>
public enum Group { Unknown, Ground, Collectible, Plant, Farm, Corpse, Chest, Boss, Enemy, Fish, Critter, Livestock }

/// <summary>What else a template is (TemplateRules). <see cref="Drop"/>: a corpse group pile a creature leaves (bones,
/// bat fur, critter parts) whose loot-all hands its default inventory over, as for the fog-nest crystals.
/// <see cref="Generic"/>: a pickup the game spawns loot as and gives its item only then (what broken barrels, pots and
/// some resource nodes drop); judged by that item (GenericDrops). <see cref="Decor"/>: a pickup the game places as
/// furniture (the books of villages and ruins) that players can also build: taken only with oldBooks on, and then not
/// while the player is in or near a base, where it may be part of the décor. <see cref="Grown"/>: a plant or ground
/// template a planted crop grows into (the world's algae, wheat, pumpkins, ...): a crop while the player is in or near a
/// base (cropsInBase, the crop radius), what it is elsewhere.</summary>
[Flags]
public enum TemplateFlags { None = 0, Stone = 1, Swarm = 2, Boss = 4, Elite = 8, Animal = 16, Drop = 32, Generic = 64, Decor = 128, Grown = 256 }

/// <summary>A template the mod may take. <paramref name="YieldMax"/>: per yield item, the most its default inventory can
/// give before the world's Loot Amount scales it (the stacks' countMax added up; null or missing = not known).</summary>
public sealed record TemplateRule(Guid Guid, Group Group, TemplateFlags Flags, string Name, IReadOnlyList<uint> Yield,
    IReadOnlyDictionary<uint, int>? YieldMax = null);

/// <summary>One item of the game (ItemRules.RowOf).</summary>
public sealed record ItemRow(uint Id, string Internal, string English, string German, bool Lootable, string Category,
    string MenuGroup, Guid Guid, int OriginalMaxStack);

/// <summary>Why a target was refused or allowed: the diagnostics counter it lands in.</summary>
public enum Verdict
{
    Allowed, UnknownTemplate, GroupOff, RadiusOut, YieldIgnored, NotReleased,
    /// <summary>Only mode: none of the target's yield is on the only list.</summary>
    NotListed,
    /// <summary>Gather focus: the target does not give the focus item.</summary>
    NotFocus,
}

/// <summary>The visit table's answer for a container target.</summary>
public enum TargetStep { Wait, Accept, Loot, AlreadyHarvested }

/// <summary>Notices the server asks a player's game to show. The game formats the text.</summary>
public enum NoticeKind : byte { InventoryFull = 1, LeftBehind = 2 }

/// <summary>A clock in milliseconds, so tests can drive time.</summary>
public interface IClock { long NowMs { get; } }

/// <summary>The slot filter's answer for one item of a container.</summary>
public enum SlotRule
{
    /// <summary>Taken: count it for the inventory-full rule (<see cref="FullTracker.ContainerAttempt"/>).</summary>
    Take,
    /// <summary>A quest item with leaveQuestItems on: stays, no notice.</summary>
    KeepQuest,
    /// <summary>On the ignore list: stays, "left behind" notice (<see cref="NoticeBook.ShouldNotify"/>).</summary>
    KeepIgnored,
    /// <summary>Only mode: not on the only list; stays, no notice.</summary>
    KeepNotListed,
    /// <summary>Gather focus is on another item; stays, no notice.</summary>
    KeepNotFocus,
    /// <summary>Gear below minGearRarity; stays, no notice.</summary>
    KeepRarity,
}
