// SPDX-License-Identifier: MIT
// The game layer of the loot loop: every Kiln call the loop makes goes through IGame, so
// tests/Offline/Server drives the whole loop with a fake world. KilnGame is the real one (tick thread only).
// The types here use only Kiln.Api's EntityId and WorldPosition, never a service assembly's type, so the offline tests
// load without Kiln's service DLLs.
using Kiln;

namespace KilnAutoLoot.Server;

/// <summary>A player in the world this step: in game, alive, with a position.</summary>
/// <param name="UiOpen">The game's own replicated UI state (PlayerStateId.InFullScreenUi or ClientUiIsInLootingMenu):
/// the window signal for players whose game has no Kiln Auto Loot.</param>
/// <param name="NearBase">In a base or near a build zone (PlayerStateId.InBase or IsNearBuildingZone): decor
/// stays.</param>
/// <param name="MayOpenInventories">The player's user group may access inventories (enshrouded_server.json
/// canAccessInventories), or the server does not know the group's rights (Player.PermissionsKnown false): farm animals,
/// which live in a base, are taken from only then.</param>
internal readonly record struct GamePlayer(EntityId Entity, WorldPosition Position, bool UiOpen, bool NearBase = false,
    bool MayOpenInventories = true);

/// <summary>Spawn.Inspect's answer: the template behind the entity and the spawn serial (ids are reused).</summary>
internal readonly record struct Inspected(Guid? Template, ulong Serial);

/// <summary>InteractionOfferRuntime: present, disabled, the current offer id.</summary>
internal readonly record struct Offer(bool Present, bool Disabled, uint OfferId)
{
    public bool Usable => Present && !Disabled;
    public static readonly Offer None = new(false, false, 0);
}

/// <summary>One non-empty inventory slot of a container. <see cref="Id"/> is Kiln's SlotId value. <see cref="Rarity"/>:
/// the slot's ItemRarity (0 common .. 4 legendary), -1 when the read gives none.</summary>
internal readonly record struct ContainerSlot(ulong Id, uint Item, int Count, int Rarity = -1);

/// <summary>A ground pickup's amount and rarity (Ecs.PickupItem pickupAmount, rarity; rarity -1 = not known).</summary>
internal readonly record struct GroundItem(int Amount, int Rarity);

/// <summary>A player's inventories as one read: every non-empty slot, and the free backpack slots (-1 = not known).</summary>
internal sealed record BagRead(IReadOnlyList<KilnAutoLoot.Core.BagSlot> Slots, int FreeSlots);

/// <summary>The line-of-sight answer.</summary>
internal enum Sight
{
    Visible,
    Hidden,
    /// <summary>The service cannot answer (held on a hosting game in dev.28, or off): treat as visible.</summary>
    Unknown,
    /// <summary>The shared ray budget of this step is spent: ask again next scan.</summary>
    Later,
}

internal enum MoveOutcome { Done, NoRoom, Failed }

internal interface IGame
{
    /// <summary>Players in game, alive, with a position (Players.All).</summary>
    IReadOnlyList<GamePlayer> Players();

    /// <summary>World.Find: every entity with the component. One map pass; the loop calls it once per scan.</summary>
    IReadOnlyList<EntityId> Find(string component);

    /// <summary>Nearby.Within: the candidates within <paramref name="radius"/> of a point, nearest first.</summary>
    IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius);

    /// <summary>World.Position.</summary>
    WorldPosition? PositionOf(EntityId entity);

    /// <summary>Spawn.Inspect.</summary>
    Inspected? Inspect(EntityId entity);

    /// <summary>Spawn.Template(guid).Name: a template's name (diagnostics only), null when unknown.</summary>
    string? TemplateName(Guid template);

    /// <summary>Ecs.PickupItem: the item id of a ground pickup.</summary>
    bool TryPickupItem(EntityId entity, out uint itemId);

    /// <summary>Ecs.PickupItem's amount and rarity; null when the entity has no pickup.</summary>
    GroundItem? PickupDetails(EntityId entity);

    /// <summary>The entity has an Inventory (mushrooms, stone piles: pickups taken with a loot-all).</summary>
    bool HasInventory(EntityId entity);

    /// <summary>Ecs.InteractionOfferRuntime.</summary>
    Offer OfferOf(EntityId entity);

    /// <summary>A dead creature: Combat.GetHealth Dead, or the loot marker the game puts on a dead enemy
    /// (DisableColliderModeChanges, transform_to_loot).</summary>
    bool IsDead(EntityId entity);

    /// <summary>Items.Read: the non-empty slots, or null when the entity has no readable inventory.</summary>
    IReadOnlyList<ContainerSlot>? Slots(EntityId container);

    /// <summary>Items.Read(player).FreeSlots(Generic); -1 when unknown.</summary>
    int FreeSlots(EntityId player);

    /// <summary>Items.Read of the player: every non-empty slot of the player's inventories (Generic = backpack) and the
    /// free backpack slots; null when it cannot be read.</summary>
    BagRead? Bag(EntityId player);

    /// <summary>The stack size the server's maxStack setting has applied in this world (0 = the game's own sizes,
    /// also when kiln.content could not apply it).</summary>
    int StackSizeWish { get; }

    /// <summary>The server can check line of sight (capability queries.line_of_sight, and no call said otherwise).</summary>
    bool SightAvailable { get; }

    /// <summary>Queries.VoxelLineOfSight from the player to the target.</summary>
    Sight LineOfSight(EntityId player, EntityId target);

    /// <summary>Gameplay.Inject.Interact: the AcceptOfferedInteractionEvent pressing E sends ("accept").</summary>
    void Interact(EntityId guest, EntityId host, uint offerId);

    /// <summary>Gameplay.Inject.Loot with lootAll and bypassInteraction ("loot-all").</summary>
    void Loot(EntityId guest, EntityId host);

    /// <summary>Items.Move of a slot into the player's inventory, <paramref name="count"/> of it (0 = the whole slot);
    /// <paramref name="done"/> runs on the tick thread when the game answered; <paramref name="moved"/> (optional) gets
    /// the item entities the move brought (gear), before done.</summary>
    void Move(EntityId from, ulong slot, EntityId to, int count, Action<MoveOutcome> done,
        Action<IReadOnlyList<EntityId>>? moved = null);
}
