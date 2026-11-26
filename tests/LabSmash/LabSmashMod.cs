// SPDX-License-Identifier: MIT
// Player-free lab probe of smashing: for a barrel, a crate, a vase, a rock boulder, a tree and a bee
// hive, spawned away from the base, route A (Combat.Kill) and route B (the mod's smash program through
// Abilities.RunProgram, Root = a spawned 1_Player character) on fresh copies each; what breaks, what joins the level
// near it (drops, a falling trunk). Then the scan cost of the mod's target search, and WorldData on kiln.content.
// Results: "labsmash: PROBE ..." lines in kiln.log; the tests pass when they ran (the probe is a measurement).
using System.Diagnostics;
using Kiln;
using Kiln.Testing;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Assert = Kiln.Testing.Assert;
using WorldData = KilnAutoLoot.Server.WorldData;

namespace KilnAutoLoot.LabSmash;

public sealed class LabSmashMod : Mod
{
    public override void OnLoad() { }
}

public sealed class Probe
{
    private static readonly (string Kind, string Template)[] Targets =
    [
        ("barrel", "Prop_Destructible_Ancients_Barrel_01"),
        ("crate", "Prop_Destructible_Fogger_Crate02_A"),
        ("vase", "Prop_Destructible_Ancients_Vase_Small_03_A"),
        ("boulder", "Prop_Destructible_Resource_Stone_Grassland_01_b"),
        ("tree", "Scatter_Loot_Grassland_Above_Tree_01_a"),
        ("beehive", "LootPickup_Food_Sweets_BeeHive_Wasp"),
    ];

    private const int Copies = 3;

    private sealed record Join(EntityId Entity, string Name, EntityCategories Category, WorldPosition? Position);

    private static void Info(string s) => KilnLog.Info($"labsmash: {s}");

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

    [KilnTest(TimeoutSeconds = 120, Order = -1)]
    public async Task Which_component_finds_targets(TestContext t)
    {
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        var names = new[] { "Destructible", "DestructibleRuntime", "ServerDestructible", "NetworkDestructible", "ServerTarget",
            "MiningNode", "MiningNodeRuntime", "NetworkMiningNode", "RandomLootOnDestroy", "RandomLootContainer", "OnDestroy",
            "Health", "NetworkHealth", "CheckPlayerInRange", "StaticCollider", "Collider", "Faction", "DestroyOnState",
            "ResourceNodePickupDrops", "CurrentTransform", "ModelComponent", "AttachToSurface", "Dissolve" };
        var spawned = new List<(string T, EntityId E)>();
        foreach (var tpl in new[] { "Prop_Destructible_Ancients_Barrel_01", "Scatter_Loot_Grassland_Above_Tree_01_a", "LootPickup_Food_Sweets_BeeHive_Wasp" })
            spawned.Add((tpl, await Spawn.Entity(tpl, Ground(a.X + 120, a.Y, a.Z + 10 * spawned.Count), 0, new SpawnOptions { Tag = "labsmash" })));
        await t.Ticks(10);
        foreach (var n in names)
        {
            var find = World.Find(n);
            Info($"PROBE component {n}: World.Find {find.Count}; has: {string.Join(", ", spawned.Select(s => $"{s.T.Split('_')[^2]}={World.Has(s.E, n)}/{World.ComponentSize(s.E, n)}"))}; " +
                 $"in Find: {string.Join(",", spawned.Select(s => find.Contains(s.E)))}");
        }
        Info($"PROBE component world: {World.EntityCount} entities");
        foreach (var (_, e) in spawned) if (World.Exists(e)) Spawn.Destroy(e);
    }

    [KilnTest(TimeoutSeconds = 300, Order = 0)]
    public async Task Followup_tree_boulder_and_scan(TestContext t)
    {
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        var joins = new List<Join>();
        var gone = new List<string>();
        using var watch = Gameplay.WatchEntities(EntityCategories.All);
        using var sub = Events.On<EntityJoinLevelEvent>(e => joins.Add(new Join(e.Entity, e.TemplateName, e.Category, e.Position)));
        using var lsub = Events.On<EntityLeaveLevelEvent>(e => gone.Add($"{e.Entity}:{e.TemplateName}"));
        Abilities.Natives.Observe("applyDamage");
        var natives = new List<string>();
        using var nsub = Events.On<ImpactNativeEvent>(e => { if (e.NativeName.Contains("pplyDamage")) natives.Add($"{e.NativeName} result {e.Result} error {e.Error}"); });
        var attacker = await Spawn.Entity("1_Player", Ground(a.X + 60, a.Y, a.Z - 6), 0, new SpawnOptions { Tag = "labsmash" });
        await t.Ticks(10);
        foreach (var template in new[] { "Scatter_Loot_Grassland_Above_Tree_01_a", "Scatter_Loot_DeepForest_Below_Tree_01_b",
                                         "Prop_Fogger_Lightplant_01_Ground_A", "Prop_Destructible_Resource_Stone_Grassland_01_b" })
        {
            var p = Ground(a.X + 150, a.Y, a.Z + 30);
            var e = await Spawn.Entity(template, p, 0, new SpawnOptions { Tag = "labsmash" });
            await t.Ticks(10);
            var find = World.Find(KilnSmashWorld.TargetComponent);
            var inFind = find.Contains(e);
            var within = Nearby.Within(find, p, 15).Contains(e);
            joins.Clear(); gone.Clear(); natives.Clear();
            var hits = new List<string>();
            for (var i = 0; i < 3 && World.Exists(e); i++)
            {
                var task = Abilities.RunProgram(SmashHit.Program, World.Position(e) ?? p, new ImpactOptions
                {
                    Root = attacker, Configs = new ImpactConfigs { [SmashHit.TargetVariable] = e }, Tag = "labsmash",
                });
                hits.Add(task.IsFaulted ? "threw" : $"impact {task.Result}");
                await t.Delay(TimeSpan.FromSeconds(3));
            }
            await t.Delay(TimeSpan.FromSeconds(8));
            var near = joins.Where(j => j.Position is { } jp && jp.DistanceTo(p) < 60 && !j.Name.StartsWith("DefaultImpact", StringComparison.Ordinal)).ToList();
            Info($"PROBE followup {template}: in World.Find(Destructible) {inFind} (of {find.Count}), Nearby.Within {within}; exists after {World.Exists(e)}; " +
                 $"hits [{string.Join("; ", hits)}]; joins within 60 m [{string.Join(", ", near.GroupBy(j => $"{j.Name}/{j.Category}").Select(g => $"{g.Key} x{g.Count()}"))}]; " +
                 $"left [{string.Join(", ", gone.Where(g => !g.Contains("DefaultImpact")).Take(8))}]; applyDamage [{string.Join("; ", natives.Take(4))}]");
            if (World.Exists(e)) Spawn.Destroy(e);
            foreach (var j in near) if (World.Exists(j.Entity)) Spawn.Destroy(j.Entity);
        }
        if (World.Exists(attacker)) Spawn.Destroy(attacker);
    }

    [KilnTest(TimeoutSeconds = 600, Order = 1)]
    public async Task Route_A_and_B_on_each_kind(TestContext t)
    {
        var (anchor, what) = FindAnchor();
        Assert.True(anchor is not null, "a flame altar or a villager to work next to");
        var a = anchor!.Value;
        Info($"anchor {what} at {a}");
        var joins = new List<Join>();
        using var watch = Gameplay.WatchEntities(EntityCategories.All);
        using var sub = Events.On<EntityJoinLevelEvent>(e => joins.Add(new Join(e.Entity, e.TemplateName, e.Category, e.Position)));
        Abilities.Natives.Observe("applyDamage");
        var damage = new List<string>();
        using var dsub = Events.On<ImpactDamageEvent>(e => damage.Add($"target {e.Target} attacker {e.Attacker} blocked {e.Blocked} result {e.Result}"));

        // the attacker: a player character (not a real player: no client, not in Players.All)
        var playerPos = Ground(a.X + 60, a.Y, a.Z - 6);
        EntityId attacker = EntityId.None;
        try { attacker = await Spawn.Entity("1_Player", playerPos, 0, new SpawnOptions { Tag = "labsmash" }); }
        catch (Exception e) { Info($"1_Player spawn failed: {e.Message}"); }
        Info($"attacker 1_Player {attacker} at {playerPos}; smash program registered: {SmashHit.Ready()}");
        await t.Ticks(10);

        var row = 0;
        foreach (var (kind, template) in Targets)
        {
            foreach (var route in new[] { "A", "B" })
            {
                row++;
                var spawned = new List<(EntityId E, WorldPosition P)>();
                for (var i = 0; i < Copies; i++)
                {
                    var p = Ground(a.X + 60 + row * 9, a.Y, a.Z + i * 7);
                    try { spawned.Add((await Spawn.Entity(template, p, 0, new SpawnOptions { Tag = "labsmash" }), p)); }
                    catch (Exception e) { Info($"PROBE {kind} {route}: spawn of {template} failed: {e.Message}"); }
                }
                await t.Ticks(10);
                var alive = spawned.Where(s => World.Exists(s.E)).ToList();
                var before = joins.Count;
                damage.Clear();
                var states = new List<string>();
                foreach (var (e, p) in alive)
                {
                    var pos = World.Position(e) ?? p;
                    if (route == "A")
                    {
                        var health = Combat.GetHealth(e);
                        try
                        {
                            var task = Combat.Kill(e, attacker);
                            await t.Until(() => task.IsCompleted, TimeSpan.FromSeconds(5), "the kill's result");
                            var r = task.Result;
                            states.Add($"{r.State}{(r.Reason is null ? "" : $" ({r.Reason})")} health {health?.Current.ToString() ?? "none"}");
                        }
                        catch (Exception ex) { states.Add($"threw {ex.GetBaseException().Message}"); }
                    }
                    else
                    {
                        var task = Abilities.RunProgram(SmashHit.Program, pos, new ImpactOptions
                        {
                            Root = attacker,
                            Configs = new ImpactConfigs { [SmashHit.TargetVariable] = e },
                            Tag = "labsmash",
                        });
                        states.Add(task.IsFaulted ? $"threw {task.Exception?.GetBaseException().Message}" : $"impact {task.Result}");
                    }
                }
                await t.Delay(TimeSpan.FromSeconds(4));
                var broke = alive.Count(s => !World.Exists(s.E));
                var mine = spawned.Select(s => s.E).ToHashSet();
                var near = joins.Skip(before)
                    .Where(j => !mine.Contains(j.Entity) && j.Position is { } jp && alive.Any(s => jp.DistanceTo(s.P) < 12))
                    .Where(j => !j.Name.StartsWith("DefaultImpact", StringComparison.Ordinal))
                    .ToList();
                var pickups = near.Where(j => j.Category.HasFlag(EntityCategories.Pickup)).ToList();
                var others = near.Where(j => !j.Category.HasFlag(EntityCategories.Pickup)).ToList();
                Info($"PROBE {kind} route {route} {template}: spawned {spawned.Count}, alive {alive.Count}, broke {broke}/{alive.Count}; " +
                     $"pickups {pickups.Count} [{string.Join(", ", pickups.GroupBy(j => j.Name).Select(g => $"{g.Key} x{g.Count()}"))}]; " +
                     $"other joins {others.Count} [{string.Join(", ", others.GroupBy(j => $"{j.Name}/{j.Category}").Select(g => $"{g.Key} x{g.Count()}"))}]; " +
                     $"hits [{string.Join("; ", states)}]; applyDamage [{string.Join("; ", damage.Take(3))}]");
                foreach (var (e, _) in spawned) if (World.Exists(e)) Spawn.Destroy(e);
                foreach (var j in near) if (World.Exists(j.Entity)) Spawn.Destroy(j.Entity);
            }
        }
        if (!attacker.IsNone && World.Exists(attacker)) Spawn.Destroy(attacker);
        Spawn.CleanupTracked("labsmash");
    }

    [KilnTest(TimeoutSeconds = 60, Order = 2)]
    public async Task Scan_cost_of_the_target_search(TestContext t)
    {
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        // each spawned target with the kind the smash table must give its template
        var spawned = new List<(string Template, SmashKind Kind, EntityId E)>();
        foreach (var (tpl, kind) in new[] { ("Prop_Destructible_Ancients_Barrel_01", SmashKind.Smash),
                     ("Prop_Destructible_Furniture_Barrel02_A_Wood_Water", SmashKind.Shared),
                     ("Prop_Destructible_Resource_Stone_Grassland_01_b", SmashKind.Stone),
                     ("Scatter_Loot_Grassland_Above_Tree_01_a", SmashKind.Tree), ("LootPickup_Food_Sweets_BeeHive_Wasp", SmashKind.Beehive) })
            spawned.Add((tpl, kind, await Spawn.Entity(tpl, Ground(a.X + 5 + 6 * spawned.Count, a.Y, a.Z + 20), 0, new SpawnOptions { Tag = "labsmash" })));
        await t.Ticks(10);
        var facts = KilnAutoLoot.Server.GameData.GameDataReader.Read(out var report);
        var table = SmashTable.FromKinds(KilnAutoLoot.Core.GameRules.Classify(facts).Smash);
        t.Log.Info($"game data read in {report.TemplateMs + report.ItemMs:0} ms: {table.Count} smash targets");
        var world = new KilnSmashWorld();
        var sw = Stopwatch.StartNew();
        var cands = world.Candidates();
        var tFind = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var near = world.Within(cands, a, 50);
        var tWithin = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var kinds = new Dictionary<SmashKind, int>();
        var unknown = 0;
        foreach (var e in near)
            if (world.Inspect(e) is { } tg && table.TryGet(tg.Template, out var k)) kinds[k] = kinds.GetValueOrDefault(k) + 1;
            else unknown++;
        var tInspect = sw.Elapsed.TotalMilliseconds;
        // per spawned target: in the candidates, near the anchor, and inspected to its template's kind
        var found = spawned.Select(s =>
        {
            SmashKind? got = world.Inspect(s.E) is { } tg && table.TryGet(tg.Template, out var k) ? k : null;
            return (s.Template, s.Kind, InCandidates: cands.Contains(s.E), Near: near.Contains(s.E), Got: got);
        }).ToList();
        Info($"PROBE spawned: {string.Join("; ", found.Select(f => $"{f.Template} candidate={f.InCandidates} near={f.Near} kind={f.Got?.ToString() ?? "none"} (want {f.Kind})"))}");
        Info($"PROBE scan: KilnSmashWorld.Candidates ({KilnSmashWorld.TargetComponent} + {KilnSmashWorld.ExtraComponent}) {cands.Count} entities in {tFind:F2} ms; Nearby.Within 50 m of the anchor {near.Count} in {tWithin:F2} ms; " +
             $"Inspect of those {tInspect:F2} ms: {string.Join(", ", kinds.Select(kv => $"{kv.Key} {kv.Value}"))}, not in the table {unknown}; " +
             $"save zone at the anchor (no player): n/a");
        foreach (var s in spawned) if (World.Exists(s.E)) Spawn.Destroy(s.E);
        Assert.True(table.Count > 1000, "the smash table loaded");
        Info($"PROBE classified near the anchor (scenery included): {kinds.Values.Sum()}");
        foreach (var f in found)
        {
            Assert.True(f.InCandidates, $"{f.Template} is among the candidates");
            Assert.True(f.Near, $"{f.Template} is found near the anchor");
            Assert.True(f.Got == f.Kind, $"{f.Template} is classified {f.Kind} (got {f.Got?.ToString() ?? "none"})");
        }
    }

    [KilnTest(TimeoutSeconds = 60, Order = 3)]
    public async Task World_data_stacks_and_backpack(TestContext t)
    {
        Assert.True(Content.Available, "kiln.content is on");
        await t.Until(() => Content.Ready, TimeSpan.FromSeconds(20), "content ready");
        var wood = Assert.NotNull(Content.Items.Get("Material_T1_Wood_Logs"), "wood logs");
        var sword = Content.Items.FirstOrDefault(i => i.Original("maxStackSize").Integer == 1);
        var big = Content.Items.OrderByDescending(i => i.Original("maxStackSize").Integer).First();
        var game = (int)wood.Original("maxStackSize").Integer;
        string Backpacks() => string.Join(", ", Content.Recipes.Where(r => WorldDataRules.IsBackpackRecipe(r.Name))
            .Select(r => $"{r.Name}: {string.Join("+", r.Inputs.Select(i => i.Count))} -> {string.Join("+", r.Outputs.Select(o => $"{o.Item}x{o.Count}"))}"));
        Info($"PROBE content before: wood {wood.MaxStack} (game {game}), largest game stack {big.Name} {big.Original("maxStackSize").Integer}; backpacks {Backpacks()}");

        WorldData.Apply(new ServerSettings { MaxStack = 9999, BackpackCheat = true });
        await t.Until(() => wood.MaxStack == 9999, TimeSpan.FromSeconds(5), "wood at 9999");
        Assert.Equal(9999, wood.MaxStack);
        if (sword is not null) Assert.Equal(1, sword.MaxStack);
        Assert.True(big.MaxStack >= big.Original("maxStackSize").Integer, "never below the game's own");
        var bp = Content.Recipes.Where(r => WorldDataRules.IsBackpackRecipe(r.Name)).ToList();
        Assert.True(bp.Count >= 4, $"backpack recipes found: {bp.Count}");
        foreach (var r in bp)
        {
            Assert.True(r.Outputs.All(o => o.Count == 1 && Items.Get(o.Item)?.Name == WorldDataRules.BestBackpack), $"{r.Name} gives the best backpack");
            Assert.True(r.Inputs.All(i => i.Count <= 1), $"{r.Name} costs 1 of each");
        }
        Info($"PROBE content applied: wood {wood.MaxStack}, stack guard {Content.StackGuard}; backpacks {Backpacks()}");

        WorldData.Apply(new ServerSettings { MaxStack = 9999, BackpackCheat = true });   // idempotent
        Assert.Equal(9999, wood.MaxStack);
        WorldData.Apply(new ServerSettings { MaxStack = 500, BackpackCheat = false });
        Assert.Equal(Math.Max(500, game), wood.MaxStack);
        Assert.True(bp.All(r => r.Outputs.All(o => Items.Get(o.Item)?.Name != WorldDataRules.BestBackpack) || r.Name == "Special_Backpack_T3"),
            "the game's recipes are back");
        WorldData.Apply(new ServerSettings { MaxStack = 0 });
        Assert.Equal(game, wood.MaxStack);
        WorldData.Reset();
        Assert.Equal(game, wood.MaxStack);
        Info($"PROBE content reverted: wood {wood.MaxStack}; backpacks {Backpacks()}");
    }
}
