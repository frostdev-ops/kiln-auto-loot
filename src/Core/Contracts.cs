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

/// <summary>A template's loot group (TemplateRules). <see cref="Unknown"/>: a group word the fixtures do not know.</summary>
public enum Group { Unknown, Ground, Collectible, Plant, Farm, Corpse, Chest, Boss, Enemy, Fish }

/// <summary>What else a template is (TemplateRules).</summary>
[Flags]
public enum TemplateFlags { None = 0, Stone = 1, Swarm = 2, Boss = 4, Elite = 8, Animal = 16 }

public sealed record TemplateRule(Guid Guid, Group Group, TemplateFlags Flags, string Name, IReadOnlyList<uint> Yield);

/// <summary>One item of the game (ItemRules.RowOf).</summary>
public sealed record ItemRow(uint Id, string Internal, string English, string German, bool Lootable, string Category,
    string MenuGroup, Guid Guid, int OriginalMaxStack);

/// <summary>Why a target was refused or allowed: the diagnostics counter it lands in.</summary>
public enum Verdict { Allowed, UnknownTemplate, GroupOff, RadiusOut, YieldIgnored, NotReleased }

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
}
