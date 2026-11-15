// SPDX-License-Identifier: MIT
// The game layer of the loot loop: every Kiln call the loop and the hold-E guard make goes through IGame, so
// tests/Offline/Server drives the whole loop with a fake world. KilnGame is the real one (tick thread only).
// The types here use only Kiln.Api's EntityId and WorldPosition, never a service assembly's type, so the offline tests
// load without Kiln's service DLLs.
using Kiln;

namespace KilnAutoLoot.Server;

/// <summary>A player in the world this step: in game, alive, with a position.</summary>
/// <param name="UiOpen">The game's own replicated UI state (PlayerStateId.InFullScreenUi or ClientUiIsInLootingMenu):
/// the window signal for players whose game has no Kiln Auto Loot.</param>
internal readonly record struct GamePlayer(EntityId Entity, WorldPosition Position, bool UiOpen);

/// <summary>Spawn.Inspect's answer: the template behind the entity and the spawn serial (ids are reused).</summary>
internal readonly record struct Inspected(Guid? Template, ulong Serial);

/// <summary>InteractionOfferRuntime: present, disabled, the current offer id.</summary>
internal readonly record struct Offer(bool Present, bool Disabled, uint OfferId)
{
    public bool Usable => Present && !Disabled;
    public static readonly Offer None = new(false, false, 0);
}

/// <summary>One non-empty inventory slot of a container. <see cref="Id"/> is Kiln's SlotId value.</summary>
internal readonly record struct ContainerSlot(ulong Id, uint Item, int Count);

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

    /// <summary>Ecs.PickupItem: the item id of a ground pickup.</summary>
    bool TryPickupItem(EntityId entity, out uint itemId);

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

    /// <summary>Queries.VoxelLineOfSight from the player to the target.</summary>
    Sight LineOfSight(EntityId player, EntityId target);

    /// <summary>Gameplay.Inject.Interact: the AcceptOfferedInteractionEvent pressing E sends ("accept").</summary>
    void Interact(EntityId guest, EntityId host, uint offerId);

    /// <summary>Gameplay.Inject.Loot with lootAll and bypassInteraction ("loot-all").</summary>
    void Loot(EntityId guest, EntityId host);

    /// <summary>Items.Move of one whole slot into the player's inventory; <paramref name="done"/> runs on the tick thread
    /// when the game answered.</summary>
    void Move(EntityId from, ulong slot, EntityId to, Action<MoveOutcome> done);
}
