// SPDX-License-Identifier: MIT
// The real game layer: Kiln 0.2.0-dev.28 calls. Tick thread only; server world only.
using Kiln;

// KILN121: LootServer checks the capabilities the loop needs before it starts (LootServer.Needed); the optional ones
// are checked here once per world.
#pragma warning disable KILN121

namespace KilnAutoLoot.Server;

internal sealed class KilnGame(Action<string> logOnce) : IGame
{
    private bool _sightUnknownLogged;
    private readonly bool _items = Capabilities.Is("kiln.items");
    private readonly bool _moves = Capabilities.Is("items.move");
    private readonly bool _combat = Capabilities.Is("kiln.combat");
    private readonly bool _sight = Capabilities.Is("queries.line_of_sight");

    public IReadOnlyList<GamePlayer> Players()
    {
        var all = Kiln.Players.All;
        var list = new List<GamePlayer>(all.Count);
        foreach (var p in all)
        {
            if (p.State != PlayerState.InGame || p.IsDead || p.Position is not { } at) continue;
            var ui = p.Is(PlayerStateId.InFullScreenUi) || p.Is(PlayerStateId.ClientUiIsInLootingMenu);
            list.Add(new GamePlayer(p.Entity, at, ui));
        }
        return list;
    }

    public IReadOnlyList<EntityId> Find(string component) => World.Find(component);

    public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
        Nearby.Within(candidates, at, radius);

    public WorldPosition? PositionOf(EntityId entity) => World.Position(entity);

    public Inspected? Inspect(EntityId entity) =>
        Spawn.Inspect(entity) is { } info ? new Inspected(info.Template, info.Serial) : null;

    public bool TryPickupItem(EntityId entity, out uint itemId)
    {
        if (Ecs.TryGet<Ecs.PickupItem>(entity, out var pi))
        {
            itemId = pi.itemId;
            return true;
        }
        itemId = 0;
        return false;
    }

    public bool HasInventory(EntityId entity) => Ecs.Has<Ecs.Inventory>(entity);

    public Offer OfferOf(EntityId entity) =>
        Ecs.TryGet<Ecs.InteractionOfferRuntime>(entity, out var o)
            ? new Offer(true, Flag(o.isDisabled), o.offerId)
            : Offer.None;

    public bool IsDead(EntityId entity) =>
        (_combat && Combat.GetHealth(entity) is { Dead: true }) || Ecs.Has<Ecs.DisableColliderModeChanges>(entity);

    public IReadOnlyList<ContainerSlot>? Slots(EntityId container)
    {
        if (!_items || Items.Read(container) is not { } inv) return null;
        var list = new List<ContainerSlot>();
        foreach (var s in inv.Slots)
            if (!s.IsEmpty && s.Count > 0) list.Add(new ContainerSlot(s.Slot.Value, s.Item.Value, s.Count));
        return list;
    }

    public int FreeSlots(EntityId player) => _items && Items.Read(player) is { } inv ? inv.FreeSlots(InventoryCategory.Generic) : -1;

    public Sight LineOfSight(EntityId player, EntityId target)
    {
        var r = _sight ? Queries.VoxelLineOfSight(player, target) : null;
        if (r is { Ok: true }) return r.Visible ? Sight.Visible : Sight.Hidden;
        if (r?.Reason == "BudgetExceeded") return Sight.Later;
        if (!_sightUnknownLogged)
        {
            _sightUnknownLogged = true;
            logOnce($"line of sight is not available here ({r?.Reason ?? Capabilities.Why("queries.line_of_sight")}): every target counts as visible");
        }
        return Sight.Unknown;
    }

    public void Interact(EntityId guest, EntityId host, uint offerId) => Gameplay.Inject.Interact(guest, host, offerId);

    public void Loot(EntityId guest, EntityId host) =>
        Gameplay.Inject.Loot(guest, host, 0, lootAll: true, bypassInteraction: true);

    public void Move(EntityId from, ulong slot, EntityId to, Action<MoveOutcome> done)
    {
        var scheduler = AutoLootMod.Scheduler;
        if (!_moves)
        {
            done(MoveOutcome.Failed);
            return;
        }
        var task = Items.Move(from, new SlotId(slot), to);
        task.ContinueWith(t =>
        {
            var outcome = t.IsCompletedSuccessfully
                ? t.Result.Status switch
                {
                    ItemsStatus.Done => MoveOutcome.Done,
                    ItemsStatus.NoRoom => MoveOutcome.NoRoom,
                    ItemsStatus.Partial => MoveOutcome.NoRoom,
                    _ => MoveOutcome.Failed,
                }
                : MoveOutcome.Failed;
            if (scheduler is not null) scheduler.Post(() => done(outcome));
        }, TaskScheduler.Default);
    }

    private static bool Flag(bool b) => b;
    private static bool Flag(byte b) => b != 0;
}
