// SPDX-License-Identifier: MIT
// Plants and corpses with "keep bag slots free" (Celphia26, 2026-11-26: with keepFreeSlots above 0 plants and corpses are
// not collected, ground items are). Player-free lab probe on a dedicated lab server:
//   - Facts: what the game shows of each kind of target the mod harvests or loots whole (a bush, a reed, a ripe crop,
//     the piles creatures leave, enemy corpses, a dead wolf): its components, its inventory as Items.Read sees it,
//     and what a loot-all (Gameplay.Inject.Loot with bypass, the mod's KilnGame.Loot) hands a 1_Player stand-in.
//   - Loop: the mod's own LootLoop with the real game layer (KilnGame) for the stand-in, with keepFreeSlots 0 and 3 and
//     the stand-in's backpack filled to a chosen number of free slots: which targets it takes, and the free slots after.
// A spawned 1_Player is not a real player (not in Players.All, an accept moves no offer on), so the stand-in is handed
// to the loop by a wrapper of KilnGame that shows no offer: every target goes without the accept a real player sends first.
// "labloot: ..." lines in kiln.log; the tests pass when they ran (the probe is a measurement).
using Kiln;
using Kiln.Testing;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Assert = Kiln.Testing.Assert;

namespace KilnAutoLoot.LabLoot;

public sealed class LabLootMod : Mod
{
    internal static string DataDirectory = "";

    public override void OnLoad()
    {
        AutoLootMod.Scheduler = Context.Scheduler;
        AutoLootMod.ModContext = Context;
        DataDirectory = Context.DataDirectory;
    }
}

public sealed class Probe
{
    private const string Tag = "labloot";

    private static void Info(string s) => KilnLog.Info($"labloot: {s}");

    /// <summary>What the probe spawns: the kind (as the player would name it) and the game's template.</summary>
    private static readonly (string Kind, string Template)[] Targets =
    [
        ("bush", "LootPickup_Material_Wood_Bush_Twigs_b"),
        ("berry bush", "Scatter_Loot_DeepForest_Above_Bush_Blackberry_01_a"),
        ("reed", "Scatter_Loot_Grassland_Reed_01_a"),
        ("crop", "T1_Plant_Growth_Corn_2_harvest"),
        ("hazelnut", "Scatter_Loot_DeepWoods_Hazelnut_d_nuts_harvest"),
        ("bones pile", "LootPickup_Material_Skeleton_Hound_Bones"),
        ("bat fur pile", "LootPickup_Material_Fur_Patch_BatLoot_1_3"),
        ("critter parts pile", "LootPickup_Material_Critter_Parts"),
        ("skeleton corpse", "LootContainer_Enemy_Skeleton_Melee"),
        ("catdeer corpse", "LootContainer_Enemy_Wildlife_CatDeer_Leather"),
        ("scavenger corpse", "LootContainer_Enemy_Scavenger_Melee01"),
    ];

    private const string Wolf = "Enemy_Wildbeast_wolf_earlygame";

    private static (WorldPosition?, string) FindAnchor()
    {
        foreach (var filter in new[] { "FlameAltar", "Altar", "Townsfolk" })
            foreach (var id in Spawn.FindByName(filter, max: 4))
                if (Spawn.Inspect(id) is { } info)
                    return (info.Position, $"{info.TemplateName} (entity {id})");
        return (null, "none");
    }

    private static WorldPosition Ground(double x, double y, double z)
    {
        var r = Queries.GroundBelow(new WorldPosition(x, y + 40, z), 120);
        return r is { Ok: true, Hit: { } h } ? new WorldPosition(x, h.Position.Y + 0.05, z) : new WorldPosition(x, y, z);
    }

    private static Data? _data;

    private static Data LoadData()
    {
        if (_data is { Ready: true }) return _data;
        var data = new Data();
        var ok = KilnAutoLoot.Server.GameData.GameDataLoader.Finish(data, LabLootMod.DataDirectory, Info, e => Info($"game data failed: {e}"));
        Info($"game data: ok {ok}, ready {data.Ready}, {data.Templates.Count} templates, {data.Items.All.Count} items");
        return _data = data;
    }

    private static string Name(uint item) =>
        _data is { } d && d.Items.TryGet(item, out var row) ? (row.English.Length > 0 ? row.English : row.Internal) : item.ToString();

    private static string InventoryText(EntityId e)
    {
        var has = World.Has(e, "Inventory");
        var inv = Items.Read(e);
        if (inv is null) return $"Inventory component {has}; Items.Read null";
        var full = inv.Slots.Where(s => !s.IsEmpty).ToList();
        return $"Inventory component {has}; Items.Read initialized {inv.Initialized}, generic {inv.GenericSlots}, available {inv.AvailableSlots}, " +
               $"free {inv.FreeSlots()}, items [{string.Join(", ", full.Select(s => $"{Name(s.Item.Value)} x{s.Count}"))}]";
    }

    /// <summary>The stand-in's items by id (every slot) and its free backpack slots.</summary>
    private static (Dictionary<uint, int> Items, int Free) Bag(EntityId p)
    {
        var inv = Items.Read(p);
        var d = new Dictionary<uint, int>();
        if (inv is null) return (d, -1);
        foreach (var s in inv.Slots)
            if (!s.IsEmpty) d[s.Item.Value] = d.GetValueOrDefault(s.Item.Value) + s.Count;
        return (d, inv.FreeSlots());
    }

    /// <summary>Free slots as the mod counts them (KilnGame.Bag: the backpack, the action bars left out) and as every
    /// Generic slot.</summary>
    private static string Free(EntityId p) => $"backpack {new KilnGame(_ => { }).FreeSlots(p)} / generic {Items.Read(p)?.FreeSlots() ?? -1}";

    private static string Diff(Dictionary<uint, int> before, Dictionary<uint, int> after) =>
        string.Join(", ", after.Where(kv => kv.Value > before.GetValueOrDefault(kv.Key))
            .Select(kv => $"{Name(kv.Key)} +{kv.Value - before.GetValueOrDefault(kv.Key)}"));

    private static async Task<EntityId> StandIn(TestContext t, WorldPosition at)
    {
        var p = await Spawn.Entity("1_Player", at, 0, new SpawnOptions { Tag = Tag, Flags = SpawnFlags.Transient });
        await t.Until(() => Items.Read(p) is { Initialized: true }, TimeSpan.FromSeconds(10), "the stand-in's inventory");
        return p;
    }

    private static async Task<EntityId> DeadWolf(TestContext t, WorldPosition at, EntityId attacker)
    {
        var w = await Spawn.Entity(Wolf, at, 0, new SpawnOptions { Tag = Tag });
        await t.Ticks(10);
        try
        {
            var kill = Combat.Kill(w, attacker);
            await t.Until(() => kill.IsCompleted, TimeSpan.FromSeconds(5), "the kill");
            Info($"wolf {w}: kill {kill.Result.State} {kill.Result.Reason}");
        }
        catch (Exception e) { Info($"wolf {w}: kill threw {e.GetBaseException().Message}"); }
        await t.Delay(TimeSpan.FromSeconds(3));
        return w;
    }

    [KilnTest(TimeoutSeconds = 300, Order = 0)]
    public async Task Facts(TestContext t)
    {
        LoadData();
        var (anchor, what) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        Info($"FACTS anchor {what} at {a}");
        var standAt = Ground(a.X + 80, a.Y, a.Z + 40);
        var p = await StandIn(t, standAt);
        Info($"FACTS stand-in {p}: {InventoryText(p)}");
        var list = Targets.ToList();
        list.Add(("dead wolf", Wolf));
        var i = 0;
        foreach (var (kind, template) in list)
        {
            i++;
            var at = Ground(standAt.X + 2 + (i % 4), standAt.Y, standAt.Z + 1.5 * (i / 4));
            EntityId e;
            try
            {
                e = kind == "dead wolf" ? await DeadWolf(t, at, p) : await Spawn.Entity(template, at, 0, new SpawnOptions { Tag = Tag });
            }
            catch (Exception ex)
            {
                Info($"FACTS {kind} {template}: spawn failed: {ex.GetBaseException().Message}");
                continue;
            }
            await t.Delay(TimeSpan.FromSeconds(1.5));
            var tpl = Spawn.Inspect(e)?.Template;
            var rule = tpl is { } g && _data!.Templates.TryGet(g, out var r) ? $"{r.Group} {r.Flags} yield [{string.Join(", ", r.Yield.Select(Name))}]" : "not in the table";
            var offer = Ecs.TryGet<Ecs.InteractionOfferRuntime>(e, out var o) ? $"offer {o.offerId} disabled {o.isDisabled}" : "no offer";
            var dead = Combat.GetHealth(e) is { } hp ? $"health {hp.Current} dead {hp.Dead}" : "no health";
            Info($"FACTS {kind} {template} {e}: table {rule}; DefaultInventory {World.Has(e, "DefaultInventory")}, " +
                 $"RandomLoot {World.Has(e, "RandomLoot")}, {offer}, {dead}, DisableColliderModeChanges {World.Has(e, "DisableColliderModeChanges")}; {InventoryText(e)}");
            var (before, free0) = Bag(p);
            Gameplay.Inject.Loot(p, e, 0, lootAll: true, bypassInteraction: true);
            await t.Delay(TimeSpan.FromSeconds(1.5));
            var (after, free1) = Bag(p);
            Info($"FACTS {kind} loot-all: the stand-in got [{Diff(before, after)}], free slots {free0} -> {free1}; " +
                 $"target exists after {World.Exists(e)}{(World.Exists(e) ? $"; {InventoryText(e)}" : "")}");
            if (World.Exists(e)) Spawn.Destroy(e);
        }
        Spawn.Destroy(p);
        Spawn.CleanupTracked(Tag);
    }

    /// <summary>Items.Move of a corpse's slots into the stand-in with 40 free slots, then with 3: what the service
    /// answers (the loop's slot moves when a bag rule keeps something in a corpse).</summary>
    [KilnTest(TimeoutSeconds = 120, Order = 2)]
    public async Task Moves_into_the_stand_in(TestContext t)
    {
        var data = LoadData();
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        foreach (var free in new[] { 40, 8, 6, 5, 4, 3 })
        {
            var at = Ground(a.X + 40, a.Y, a.Z - 40 - free);
            var p = await StandIn(t, at);
            var fillers = data.Items.All.Where(x => x.OriginalMaxStack > 1 && x.Category == "Materials").Select(x => x.Id).ToList();
            var k = 0;
            while (Items.Read(p)!.FreeSlots() > free && k < fillers.Count) await Items.Give(p, new ItemId(fillers[k++]), 1);
            var corpse = await Spawn.Entity("LootContainer_Enemy_Skeleton_Melee", Ground(at.X + 2, at.Y, at.Z), 0, new SpawnOptions { Tag = Tag });
            await t.Delay(TimeSpan.FromSeconds(1.5));
            var pi = Items.Read(p)!;
            var bars = Ecs.TryGet<Ecs.SlotSelection>(p, out var sel)
                ? $"action bars {sel.actionbarEntityIds[0].id}, {sel.actionbarEntityIds[1].id}" : "no SlotSelection";
            Info($"MOVE {bars}; inventories [{string.Join(", ", pi.Inventories.Select((x, i) => $"{i}:{x.Inventory}/{x.Category}"))}]");
            Info($"MOVE free {pi.FreeSlots()}: links [{string.Join(", ", pi.Inventories.Select((x, i) => $"{i}:{x.Category}"))}]; empty slots by link " +
                 $"[{string.Join(", ", pi.Slots.Where(x => x.IsEmpty).GroupBy(x => (x.Link, x.Category)).Select(g => $"{g.Key.Link}/{g.Key.Category} x{g.Count()}"))}]; " +
                 $"used by link [{string.Join(", ", pi.Slots.Where(x => !x.IsEmpty).GroupBy(x => (x.Link, x.Category)).Select(g => $"{g.Key.Link}/{g.Key.Category} x{g.Count()}"))}]; corpse {InventoryText(corpse)}");
            foreach (var slot in Items.Read(corpse)!.Slots.Where(x => !x.IsEmpty).ToList())
            {
                var r = await Items.Move(corpse, slot.Slot, p);
                Info($"MOVE free before {free}: {Name(slot.Item.Value)} x{slot.Count}: {r.Status} done {r.Done} '{r.Message}' game error {r.GameError}; stand-in free now {Items.Read(p)!.FreeSlots()}");
            }
            Spawn.Destroy(corpse);
            Spawn.Destroy(p);
        }
        Spawn.CleanupTracked(Tag);
    }

    /// <summary>KilnGame with the stand-in as the only player, and every loot-all and slot move counted per target.</summary>
    private sealed class StandInGame(KilnGame inner, EntityId player) : IGame
    {
        public readonly Dictionary<EntityId, int> Loots = new();
        public readonly Dictionary<EntityId, int> Moves = new();

        public IReadOnlyList<GamePlayer> Players() =>
            World.Position(player) is { } at ? [new GamePlayer(player, at, false)] : [];

        public IReadOnlyList<EntityId> Find(string component) => inner.Find(component);
        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) => inner.Within(candidates, at, radius);
        public WorldPosition? PositionOf(EntityId entity) => inner.PositionOf(entity);
        public Inspected? Inspect(EntityId entity) => inner.Inspect(entity);
        public long SpawnStamp(EntityId entity) => inner.SpawnStamp(entity);
        public string? TemplateName(Guid template) => inner.TemplateName(template);
        public bool TryPickupItem(EntityId entity, out uint itemId) => inner.TryPickupItem(entity, out itemId);
        public GroundItem? PickupDetails(EntityId entity) => inner.PickupDetails(entity);
        public bool HasInventory(EntityId entity) => inner.HasInventory(entity);
        // a stand-in's accept moves no offer on (offers wait for a real player), so the loop would send accepts until it
        // gives up on them; with no usable offer it goes straight to the loot-all, as after a real player's accept
        public Offer OfferOf(EntityId entity) => Offer.None;
        public bool IsDead(EntityId entity) => inner.IsDead(entity);
        public IReadOnlyList<ContainerSlot>? Slots(EntityId container) => inner.Slots(container);
        public int FreeSlots(EntityId p) => inner.FreeSlots(p);
        public BagRead? Bag(EntityId p) => inner.Bag(p);
        public int StackSizeWish => inner.StackSizeWish;
        public bool SightAvailable => inner.SightAvailable;
        public SightAnswer LineOfSight(EntityId p, EntityId target, SightSample sample = SightSample.Primary, bool corpse = false) =>
            inner.LineOfSight(p, target, sample, corpse);
        public void Interact(EntityId guest, EntityId host, uint offerId) => inner.Interact(guest, host, offerId);

        public void Loot(EntityId guest, EntityId host)
        {
            Loots[host] = Loots.GetValueOrDefault(host) + 1;
            inner.Loot(guest, host);
        }

        public void Move(EntityId from, ulong slot, EntityId to, int count, Action<MoveAnswer> done, Action<IReadOnlyList<EntityId>>? moved = null)
        {
            Moves[from] = Moves.GetValueOrDefault(from) + 1;
            inner.Move(from, slot, to, count, done, moved);
        }
    }

    [KilnTest(TimeoutSeconds = 400, Order = 1)]
    public async Task Loop_keep_free_slots(TestContext t)
    {
        var data = LoadData();
        Assert.True(data.Ready, "the game data");
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        // keep, free slots left after the fill
        var cases = new (int Keep, int Free)[] { (0, 12), (3, 12), (3, 4), (3, 3) };
        var row = 0;
        foreach (var (keep, free) in cases)
        {
            row++;
            var standAt = Ground(a.X - 80, a.Y, a.Z + 30 * row);
            var p = await StandIn(t, standAt);
            // fill the backpack to `free` empty slots with one each of materials nothing here gives
            var yields = new HashSet<uint>();
            var names = Targets.Select(x => x.Template).ToHashSet(StringComparer.Ordinal);
            foreach (var r in data.Templates.All)
                if (names.Contains(r.Name)) yields.UnionWith(r.Yield);
            var fillers = data.Items.All.Where(x => x.OriginalMaxStack > 1 && x.Category == "Materials" && !yields.Contains(x.Id))
                .Select(x => x.Id).ToList();
            var start = Free(p);
            var k = 0;
            var counted = new KilnGame(_ => { });
            while (counted.FreeSlots(p) > free && k < fillers.Count)
            {
                var g = await Items.Give(p, new ItemId(fillers[k++]), 1);
                if (!g.Succeeded) Info($"LOOP fill: give {Name(fillers[k - 1])} {g.Status}");
            }
            var (bag0, _) = Bag(p);
            var free0 = Free(p);
            var spawned = new List<(string Kind, EntityId E)>();
            var j = 0;
            foreach (var (kind, template) in Targets)
            {
                j++;
                var at = Ground(standAt.X + 2 + (j % 4) * 1.5, standAt.Y, standAt.Z + 1.5 * (j / 4));
                try { spawned.Add((kind, await Spawn.Entity(template, at, 0, new SpawnOptions { Tag = Tag }))); }
                catch (Exception ex) { Info($"LOOP {kind}: spawn failed {ex.GetBaseException().Message}"); }
            }
            spawned.Add(("dead wolf", await DeadWolf(t, Ground(standAt.X - 3, standAt.Y, standAt.Z), p)));
            await t.Delay(TimeSpan.FromSeconds(1.5));
            var contents = spawned.ToDictionary(s => s.E, s => InventoryText(s.E));

            var game = new StandInGame(new KilnGame(Info), p);
            var clock = new TickClock();
            var loop = new LootLoop(game, clock, data, (_, _) => { }, l => Info($"LOOP key: {l}"));
            loop.Configure(new ServerSettings());
            loop.SetPersonal(p, new PersonalSettings
            {
                KeepFreeSlots = keep, OpenDelay = 0, VisibleOnly = false, Radius = 12, EnemyPause = false, Smash = false,
            });
            var last = Environment.TickCount64;
            var until = last + 8000;
            while (Environment.TickCount64 < until)
            {
                var now = Environment.TickCount64;
                clock.Advance(now - last);
                last = now;
                loop.Step();
                await t.Ticks(1);
            }
            await t.Delay(TimeSpan.FromSeconds(1));
            var (bag1, _) = Bag(p);
            var free1 = Free(p);
            var c = loop.Counters;
            Info($"LOOP keep {keep} free {free}: free slots {start} -> {free0} after the fill -> {free1} after the loop; got [{Diff(bag0, bag1)}]; " +
                 $"counters noRoom {c.NoRoom} harvestUnread {c.HarvestUnread} unreadable {c.Unreadable} containerTries {c.ContainerTries}");
            foreach (var (kind, e) in spawned)
                Info($"LOOP keep {keep} free {free}: {kind} {e}: loot-alls {game.Loots.GetValueOrDefault(e)}, moves {game.Moves.GetValueOrDefault(e)}, " +
                     $"exists after {World.Exists(e)}; before: {contents[e]}{(World.Exists(e) ? $"; after: {InventoryText(e)}" : "")}");
            foreach (var (_, e) in spawned) if (World.Exists(e)) Spawn.Destroy(e);
            Spawn.Destroy(p);
        }
        Spawn.CleanupTracked(Tag);
    }
}
