// SPDX-License-Identifier: MIT
// In-server checks with no player: `kiln test kiln-auto-loot-labtest --server <lab>`. What a dedicated lab server can
// show: the Kiln calls Kiln Auto Loot's loot loop makes (loot-all with bypass from a chest
// into a player character, Items.Move of one slot, Inject.Loot of a ground item, LootEvent cancel), that the mod's server
// part runs, and what one scan costs. A spawned 1_Player is not a real player: it is not in Players.All and offers stay
// inactive without a real player in range, so accept-then-loot, hold-E and the window pause are client checks.
using System.Diagnostics;
using Kiln;
using Kiln.Testing;
using Assert = Kiln.Testing.Assert;

namespace KilnAutoLoot.LabTest;

public sealed class LabTestMod : Mod
{
    public override void OnLoad() { }
}

public sealed class LootLabTests
{
    private const string Tag = "kiln-auto-loot-labtest";
    private const string ChestTemplate = "LootChest_Random_Cairn_Sarcophagus_Wood";   // a world chest of the game

    // the game's item ids (Items.Lookup by name answered nothing on the lab: no catalog there)
    private static readonly ItemId Logs = new(722268629);      // Material_T1_Wood_Logs
    private static readonly ItemId Stone = new(1586398842);    // Material_T1_Stone_Flint

    /// <summary>Near a base, so the terrain is loaded (as Kiln's items-test does).</summary>
    private static WorldPosition Spot()
    {
        foreach (var a in World.Find("BaseIdComponent"))
            if (World.Position(a) is { } q) return new WorldPosition(q.X + 45, q.Y + 3, q.Z + 45);
        return new WorldPosition(3845, 835, 1480);
    }

    private static WorldPosition Off(WorldPosition p, double dx) => new(p.X + dx, p.Y, p.Z);

    private static async Task<EntityId> PlayerCharacter(TestContext t, WorldPosition at)
    {
        var p = await Spawn.Entity("1_Player", at, 0, new SpawnOptions { Tag = Tag, Flags = SpawnFlags.Transient });
        await t.Until(() => Items.Read(p) is { Initialized: true }, TimeSpan.FromSeconds(10), "the character's inventory");
        return p;
    }

    private static async Task<EntityId> Chest(TestContext t, WorldPosition at)
    {
        var c = await Spawn.Entity(ChestTemplate, at, 0, new SpawnOptions { Tag = Tag, Flags = SpawnFlags.Transient });
        await t.Until(() => Items.Read(c) is { Initialized: true }, TimeSpan.FromSeconds(15), "the chest's inventory initialized");
        return c;
    }

    private static int Count(EntityId e, ItemId item) => Items.Read(e)?.Count(item) ?? -1;

    private static void Cleanup(params EntityId[] es)
    {
        foreach (var e in es) if (!e.IsNone) Spawn.Destroy(e);
    }

    [KilnTest(Order = 0)]
    public void The_mods_server_part_runs_on_a_dedicated_server()
    {
        Assert.True(Mods.IsLoaded("kiln-auto-loot"), "kiln-auto-loot is loaded");
        Assert.NotNull(Config.Find("kiln-auto-loot-server"), "the server settings are registered (ServerStartedEvent reached the mod)");
        var lines = StatusLines.Lines("kiln-auto-loot");
        Assert.True(lines.Any(l => l.StartsWith("Kiln Auto Loot:", StringComparison.Ordinal)),
            $"the status line of the loot loop is there: [{string.Join(" | ", lines)}]");
        foreach (var c in new[] { "kiln.players", "world.find", "kiln.spawn", "gameplay.emit", "gameplay.intercept", "items.move" })
            Assert.True(Capabilities.Is(c), $"{c} is on ({Capabilities.Why(c)})");
    }

    [KilnTest(TimeoutSeconds = 40)]
    public async Task Loot_all_with_bypass_moves_a_chests_items_into_a_player_character(TestContext t)
    {
        var at = Spot();
        EntityId p = default, chest = default;
        try
        {
            p = await PlayerCharacter(t, at);
            chest = await Chest(t, Off(at, 2));
            var g = await Items.Give(chest, Logs, 2);
            Assert.True(g.Succeeded, $"2 logs into chest {chest}: {g}");
            var before = Count(p, Logs);
            Gameplay.Inject.Loot(p, chest, 0, lootAll: true, bypassInteraction: true);
            await t.Until(() => Count(p, Logs) >= before + 2, TimeSpan.FromSeconds(5), "the logs in the character's inventory");
            Assert.Equal(0, Count(chest, Logs), "the chest gave all its logs");
        }
        finally { Cleanup(chest, p); }
    }

    [KilnTest(TimeoutSeconds = 40)]
    public async Task Items_Move_takes_one_slot_and_leaves_the_others(TestContext t)
    {
        var at = Spot();
        EntityId p = default, chest = default;
        try
        {
            p = await PlayerCharacter(t, at);
            chest = await Chest(t, Off(at, 2));
            Assert.True((await Items.Give(chest, Logs, 3)).Succeeded, "3 logs into the chest");
            Assert.True((await Items.Give(chest, Stone, 4)).Succeeded, "4 flint into the chest");
            var slot = Assert.NotNull(Items.Read(chest)!.Slots.FirstOrDefault(s => !s.IsEmpty && s.Item == Logs), "the logs' slot");
            var before = Count(p, Logs);
            var r = await Items.Move(chest, slot.Slot, p);
            Assert.True(r.Status == ItemsStatus.Done, $"the move is done: {r.Status} ({r.Done} of {r.Requested})");
            Assert.Equal(before + 3, Count(p, Logs), "the character got the 3 logs");
            Assert.Equal(4, Count(chest, Stone), "the flint stayed in the chest (the slot filter's 'keep')");
        }
        finally { Cleanup(chest, p); }
    }

    [KilnTest(TimeoutSeconds = 40)]
    public async Task A_ground_item_is_taken_with_Inject_Loot(TestContext t)
    {
        var at = Spot();
        EntityId p = default, item = default;
        try
        {
            p = await PlayerCharacter(t, at);
            item = await Spawn.Item(Logs.Value, 3, Off(at, 1.5));
            await t.Ticks(5);
            Assert.True(World.Has(item, "PickupItem"), $"the spawned item {item} is a PickupItem");
            var before = Count(p, Logs);
            Gameplay.Inject.Loot(p, item, 0, lootAll: true, bypassInteraction: true);
            await t.Until(() => Count(p, Logs) >= before + 3, TimeSpan.FromSeconds(5), "the item in the character's inventory");
            await t.Until(() => !World.Exists(item), TimeSpan.FromSeconds(5), "the pickup gone from the world");
            item = default;
        }
        finally { Cleanup(item, p); }
    }

    [KilnTest(TimeoutSeconds = 40)]
    public async Task A_canceled_LootEvent_moves_nothing(TestContext t)
    {
        var at = Spot();
        EntityId p = default, chest = default;
        var seen = 0;
        try
        {
            p = await PlayerCharacter(t, at);
            chest = await Chest(t, Off(at, 2));
            Assert.True((await Items.Give(chest, Logs, 2)).Succeeded, "2 logs into the chest");
            var host = chest;
            using var sub = Events.On<LootEvent>(e =>
            {
                if (e.Host != host) return;
                seen++;
                e.Cancel();
            });
            var before = Count(p, Logs);
            Gameplay.Inject.Loot(p, chest, 0, lootAll: true, bypassInteraction: true);
            await t.Ticks(6);
            Assert.True(seen > 0, "the LootEvent was seen");
            Assert.Equal(2, Count(chest, Logs), "the chest kept its logs");
            Assert.Equal(before, Count(p, Logs), "the character got nothing");
        }
        finally { Cleanup(chest, p); }
    }

    [KilnTest(TimeoutSeconds = 30)]
    public void The_cost_of_one_scan_on_this_world(TestContext t)
    {
        // what LootLoop does per scan: four World.Find (shared by all players), then per player Nearby.Within on each list
        // at the largest search radius and Spawn.Inspect of every hit; measured where loot lies (the densest candidate)
        var at = Spot();
        var all = World.Find("InteractionOfferRuntime").Concat(World.Find("PickupItem")).ToList();
        var best = -1;
        foreach (var c in all.Take(400))
            if (World.Position(c) is { } q && Nearby.Within(all, q, 50).Count is var n && n > best) { best = n; at = q; }
        var times = new List<double>();
        int found = 0, near = 0;
        for (var run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            var lists = new[] { "PickupItem", "InteractionOfferRuntime", "Inventory", "DisableColliderModeChanges" }
                .Select(World.Find).ToList();
            found = lists.Sum(l => l.Count);
            near = 0;
            var seen = new HashSet<EntityId>();
            foreach (var l in lists)
                foreach (var e in Nearby.Within(l, at, 50))
                    if (seen.Add(e) && Spawn.Inspect(e) is not null) near++;
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        var line = $"one scan at 50 m: {times.Min():0.00}-{times.Max():0.00} ms (median {times.Order().ElementAt(2):0.00}), " +
                   $"{found} candidates in the world, {near} inspected near {at}, world {World.EntityCount} entities";
        t.Log.Info($"kiln-auto-loot-labtest: {line}");
        Assert.True(times.Min() < 100, line);
    }
}
