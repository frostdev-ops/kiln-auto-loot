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
            var nearBase = p.IsInBase || p.Is(PlayerStateId.IsNearBuildingZone);
            // a user group without "access inventories" gets no prompt at a chest: no farm animal's produce either
            var inventories = !p.PermissionsKnown || p.Can(PlayerPermissions.AccessInventories);
            list.Add(new GamePlayer(p.Entity, at, ui, nearBase, inventories));
        }
        return list;
    }

    public IReadOnlyList<EntityId> Find(string component) => World.Find(component);

    public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
        Nearby.Within(candidates, at, radius);

    public WorldPosition? PositionOf(EntityId entity) => World.Position(entity);

    public Inspected? Inspect(EntityId entity) =>
        Spawn.Inspect(entity) is { } info ? new Inspected(info.Template, info.Serial) : null;

    public string? TemplateName(Guid template) => Spawn.Template(template)?.Name;

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

    public GroundItem? PickupDetails(EntityId entity)
    {
        if (!Ecs.TryGet<Ecs.PickupItem>(entity, out var pi)) return null;
        int rarity;
        try { rarity = Convert.ToInt32(pi.rarity); }
        catch (Exception e) when (e is InvalidCastException or OverflowException) { rarity = -1; }
        return new GroundItem(Math.Max(1, (int)pi.pickupAmount), rarity);
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
            if (!s.IsEmpty && s.Count > 0)
                list.Add(new ContainerSlot(s.Slot.Value, s.Item.Value, s.Count, s.Rarity is { } r ? (int)r : -1));
        return list;
    }

    public int FreeSlots(EntityId player) => _items && Items.Read(player) is { } inv ? inv.FreeSlots(InventoryCategory.Generic) : -1;

    public BagRead? Bag(EntityId player)
    {
        if (!_items || Items.Read(player) is not { } inv) return null;
        var list = new List<KilnAutoLoot.Core.BagSlot>(inv.Slots.Count);
        foreach (var s in inv.Slots)
            if (!s.IsEmpty && s.Count > 0)
                list.Add(new KilnAutoLoot.Core.BagSlot(s.Item.Value, s.Count, s.Category == InventoryCategory.Generic));
        return new BagRead(list, inv.FreeSlots(InventoryCategory.Generic));
    }

    public int StackSizeWish => WorldData.AppliedStackSize;

    public bool SightAvailable => _sight;

    public Sight LineOfSight(EntityId player, EntityId target)
    {
        var r = _sight ? Queries.VoxelLineOfSight(player, target) : null;
        if (r is { Ok: true }) return r.Visible ? Sight.Visible : Sight.Hidden;
        if (r?.Reason == "BudgetExceeded") return Sight.Later;
        if (!_sightUnknownLogged)
        {
            _sightUnknownLogged = true;
            logOnce(SightUnknownLine(r?.Reason, r is null ? Capabilities.Why("queries.line_of_sight") : ""));
        }
        return Sight.Unknown;
    }

    /// <summary>kiln.log's line (once a world) for line of sight without an answer: <paramref name="reason"/> is the
    /// check's own (NotLoaded while a world loads, ...), null when the server has no check (<paramref name="why"/>
    /// says why). Either way a target counts as visible, and the height window decides what is in reach (0.2.8).</summary>
    internal static string SightUnknownLine(string? reason, string why) => reason is null
        ? $"line of sight is not available here ({why}): every target counts as visible, and the height window " +
          "(heightAbove, heightBelow; 0 = no limit) decides what is in reach"
        : $"line of sight did not answer ({reason}): until it answers, a target counts as visible, " +
          $"and the height window (heightAbove, heightBelow; 0 = no limit) decides what is in reach for up to " +
          $"{LootLoop.SightRetryMs / 1000} s at a time";

    public void Interact(EntityId guest, EntityId host, uint offerId) => Gameplay.Inject.Interact(guest, host, offerId);

    public void Loot(EntityId guest, EntityId host) =>
        Gameplay.Inject.Loot(guest, host, 0, lootAll: true, bypassInteraction: true);

    public void Move(EntityId from, ulong slot, EntityId to, int count, Action<MoveOutcome> done,
        Action<IReadOnlyList<EntityId>>? moved = null)
    {
        var scheduler = AutoLootMod.Scheduler;
        if (!_moves)
        {
            done(MoveOutcome.Failed);
            return;
        }
        var task = Items.Move(from, new SlotId(slot), to, Math.Max(0, count));
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
            // gear keeps its item entity through a move: the entities that arrived (salvage rules look at them)
            var entities = t.IsCompletedSuccessfully && outcome != MoveOutcome.Failed
                ? t.Result.Changes.Select(c => c.ItemEntity).Where(x => !x.IsNone).Distinct().ToList()
                : new List<EntityId>();
            if (scheduler is not null)
                scheduler.Post(() =>
                {
                    moved?.Invoke(entities);
                    done(outcome);
                });
        }, TaskScheduler.Default);
    }

    private static bool Flag(bool b) => b;
    private static bool Flag(byte b) => b != 0;
}
