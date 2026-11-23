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

    public long SpawnStamp(EntityId entity) => World.TryRead<long>(entity, "SpawnTime", out var t) ? t : 0;

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

    internal static readonly VoxelSightOptions CorpseSightOptions = new() { ToOffset = new(0, SightTrace.BodyHeight, 0) };

    internal static readonly VoxelSightOptions CorpseUpperSightOptions = new() { ToOffset = new(0, SightTrace.UpperHeight, 0) };

    public SightAnswer LineOfSight(EntityId player, EntityId target, SightSample sample = SightSample.Primary)
    {
        var options = sample switch
        {
            SightSample.CorpseBody => CorpseSightOptions,
            SightSample.CorpseUpper => CorpseUpperSightOptions,
            _ => null,
        };
        var r = _sight ? Queries.VoxelLineOfSight(player, target, options) : null;
        var answer = ReadSight(r);
        // A lower sample's Unknown cannot override the primary's known voxel hit. Its reason goes only to the watched
        // entity's /near trace, not the global "height fallback decides" line (which applies to primary Unknown).
        if (answer.State == Sight.Unknown && sample == SightSample.Primary && !_sightUnknownLogged)
        {
            _sightUnknownLogged = true;
            logOnce(SightUnknownLine(r?.Reason, r is null ? Capabilities.Why("queries.line_of_sight") : ""));
        }
        return answer;
    }

    internal static SightAnswer ReadSight(VoxelSightResult? r) => r is { Ok: true }
        ? new(r.Visible ? Sight.Visible : Sight.Hidden,
            r.Obstruction is { } h ? new SightHit(h.Position, h.Distance, h.Material, h.Voxel) : null)
        : new(r?.Reason == "BudgetExceeded" ? Sight.Later : Sight.Unknown, Reason: r?.Reason);

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

    public void Move(EntityId from, ulong slot, EntityId to, int count, Action<MoveAnswer> done,
        Action<IReadOnlyList<EntityId>>? moved = null)
    {
        var scheduler = AutoLootMod.Scheduler;
        if (!_moves || scheduler is null)
        {
            done(new(MoveOutcome.Failed, 0, "Unavailable", !_moves ? "items.move unavailable" : "no tick scheduler", 0));
            return;
        }
        Task<MoveResult> task;
        try { task = Items.Move(from, new SlotId(slot), to, Math.Max(0, count)); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            done(new(MoveOutcome.Failed, 0, ex.GetType().Name, ex.Message, 0));
            return;
        }
        task.ContinueWith(t =>
        {
            var answer = t.IsCompletedSuccessfully ? Answer(t.Result, slot)
                : new MoveAnswer(MoveOutcome.Failed, 0, t.IsCanceled ? "Canceled" : "Faulted", t.Exception?.GetBaseException().Message ?? "task canceled", 0);
            // gear keeps its item entity through a committed move (never salvage a planned/rolled-back one)
            var entities = t.IsCompletedSuccessfully && t.Result.Succeeded && !t.Result.DryRun
                ? t.Result.Changes.Where(c => c.Amount > 0).Select(c => c.ItemEntity).Where(x => !x.IsNone).Distinct().ToList()
                : new List<EntityId>();
            scheduler.Post(() =>
            {
                moved?.Invoke(entities);
                done(answer);
            });
        }, TaskScheduler.Default);
    }

    internal static MoveAnswer Answer(MoveResult result, ulong slot)
    {
        var outcome = result.Status switch
        {
            ItemsStatus.Done => MoveOutcome.Done,
            ItemsStatus.NoRoom or ItemsStatus.Partial => MoveOutcome.NoRoom,
            _ => MoveOutcome.Failed,
        };
        var confirmed = result.Succeeded && !result.DryRun
            ? result.Changes.Where(c => c.From.Value == slot && c.Amount > 0).Sum(c => c.Amount) : 0;
        return new(outcome, confirmed, result.Status.ToString(),
            $"{result.Message} (game error {result.GameError}, canceled {result.Canceled}, steps {result.Steps}, dry run {result.DryRun})", result.Done);
    }

    private static bool Flag(bool b) => b;
    private static bool Flag(byte b) => b != 0;
}
