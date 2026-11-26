// SPDX-License-Identifier: MIT
// Smash pace in the lab (HadToRegister 2026-11-26: at a stack of barrels one breaks, then 45 s to 1.5 min to the next).
// The mod's own Smash.Run (src/Server/Smash.cs) on the lab's world once per second for a spawned 1_Player stand-in, with
// the real KilnSmashWorld (candidates, Spawn.Inspect, line of sight, the smash program), driven as LootServer.SmashStep
// does (a pass a second, its rest in the next steps): among props spawned around the stand-in (a ring and stacks of
// barrels), in a room built of stone in the air (Kiln.Build, so the game's rays answer), and over a sealed cellar full of
// urns. Per target: distance, height, hits, line-of-sight answers, when it broke. A lab server loads none of the world's
// own props (no player streams them in), and its rays answer only where Kiln.Build loaded the tiles.
// "labsmash: PACE ..." lines in kiln.log; the tests pass when they ran (the probe is a measurement).
using Kiln;
using Kiln.Testing;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Assert = Kiln.Testing.Assert;

namespace KilnAutoLoot.LabSmash;

public sealed class Pace
{
    private static void Info(string s) => KilnLog.Info($"labsmash: PACE {s}");

    /// <summary>KilnSmashWorld with every answer counted per target.</summary>
    private sealed class Watched(ISmashWorld inner) : ISmashWorld
    {
        public readonly Dictionary<EntityId, Dictionary<Sight, int>> Sights = new();
        public readonly Dictionary<Sight, int> SightTotals = new();
        public int InspectCalls, CandidateCalls;

        public IReadOnlyList<EntityId> Candidates() { CandidateCalls++; return inner.Candidates(); }
        public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) => inner.Within(candidates, at, radius);
        public SmashTarget? Inspect(EntityId entity) { InspectCalls++; return inner.Inspect(entity); }
        public long SpawnStamp(EntityId entity) => inner.SpawnStamp(entity);
        public WorldPosition? PositionOf(EntityId entity) => inner.PositionOf(entity);
        public bool InSaveZone(EntityId player) { try { return inner.InSaveZone(player); } catch { return false; } }
        public bool NearBuildZone(EntityId player) { try { return inner.NearBuildZone(player); } catch { return false; } }
        public void Hit(EntityId player, in SmashTarget target, SmashKind kind) => inner.Hit(player, target, kind);
        public bool SightAvailable => inner.SightAvailable;

        public readonly Dictionary<string, int> Reasons = new();
        public readonly Dictionary<EntityId, string> Blocked = new();

        public Sight LineOfSight(EntityId player, EntityId target, double toHeight)
        {
            var s = inner.LineOfSight(player, target, toHeight);
            if (s == Sight.Hidden && !Blocked.ContainsKey(target))
            {
                var r = Queries.VoxelLineOfSight(player, target, new VoxelSightOptions { ToOffset = new WorldPosition(0, toHeight, 0) });
                Blocked[target] = r.Obstruction is { } h && World.Position(target) is { } tp
                    ? $"blocked {h.Distance:0.00} m along, at dy {h.Position.Y - tp.Y:+0.00;-0.00} from the target's origin, {Math.Sqrt(Math.Pow(h.Position.X - tp.X, 2) + Math.Pow(h.Position.Z - tp.Z, 2)):0.00} m sideways, material {h.Material}, the build says that cell is {(Build.ReadCell(Build.CellOf(h.Position)) is { } v ? (v.IsAir ? "air" : "solid") : "unread")}, ray from {World.Position(player)}, hit at {h.Position}"
                    : $"no obstruction now (ok {r.Ok} visible {r.Visible})";
            }
            if (s is Sight.Unknown or Sight.Later)
            {
                var r = Queries.VoxelLineOfSight(player, target, new VoxelSightOptions { ToOffset = new WorldPosition(0, toHeight, 0) });
                var why = $"{s}:{r.Reason ?? (r.Ok ? (r.Visible ? "visible-now" : "hidden-now") : "none")}";
                Reasons[why] = Reasons.GetValueOrDefault(why) + 1;
            }
            if (!Sights.TryGetValue(target, out var d)) Sights[target] = d = new();
            d[s] = d.GetValueOrDefault(s) + 1;
            SightTotals[s] = SightTotals.GetValueOrDefault(s) + 1;
            return s;
        }
    }

    private static SmashTable Table()
    {
        var facts = KilnAutoLoot.Server.GameData.GameDataReader.Read(out _);
        return SmashTable.FromKinds(GameRules.Classify(facts).Smash);
    }

    private static (WorldPosition?, string) FindAnchor()
    {
        foreach (var filter in new[] { "FlameAltar", "Altar", "Townsfolk" })
            foreach (var id in Spawn.FindByName(filter, max: 4))
                if (Spawn.Inspect(id) is { } info)
                    return (info.Position, $"{info.TemplateName} (entity {id})");
        return (null, "none");
    }

    private static WorldPosition Ground(double x, double y, double z, double from = 40)
    {
        var r = Queries.GroundBelow(new WorldPosition(x, y + from, z), from + 80);
        return r is { Ok: true, Hit: { } h } ? new WorldPosition(x, h.Position.Y + 0.05, z) : new WorldPosition(x, y, z);
    }

    private static string Name(Guid template) => Spawn.Template(template)?.Name ?? template.ToString();

    /// <summary>Smash.Run once a second for <paramref name="seconds"/> s (or until every watched target is gone); one
    /// PACE line per target and a summary.</summary>
    private static async Task RunPace(TestContext t, string label, EntityId stand, IReadOnlyList<SmashTarget> watched,
        SmashTable table, PersonalSettings s, int seconds)
    {
        var world = new Watched(new KilnSmashWorld());
        var smash = new global::KilnAutoLoot.Server.Smash(new Data(), world, () => Environment.TickCount64, () => table);
        var start = Environment.TickCount64;
        var hits = new Dictionary<EntityId, List<long>>();
        var gone = new Dictionary<EntityId, long>();
        var otherHits = 0;
        var watchedIds = watched.Select(w => w.Entity).ToHashSet();
        smash.Hit = (target, kind, sw) =>
        {
            if (!watchedIds.Contains(target.Entity)) { otherHits++; return; }
            if (!hits.TryGetValue(target.Entity, out var l)) hits[target.Entity] = l = [];
            l.Add(Environment.TickCount64 - start);
        };
        var at0 = World.Position(stand) ?? watched[0].Position;
        Info($"{label}: stand-in {stand} at {at0}, in save zone {world.InSaveZone(stand)}, near build zone {world.NearBuildZone(stand)}, " +
             $"sight available {world.SightAvailable}, program ready {SmashHit.Ready()}, visibleOnly {s.VisibleOnly}, radius {s.Radius}; " +
             $"{watched.Count} watched targets");
        var runs = new List<string>();
        // LootServer.SmashStep: a new pass once a second, the rest of an unfinished pass in the next server steps
        var next = Environment.TickCount64;
        var resumes = 0;
        for (var i = 0; i < seconds;)
        {
            var at = World.Position(stand) ?? at0;
            smash.BeginStep(1);   // as LootServer.SmashStep: a server step's smash time and rays start from nothing
            if (Environment.TickCount64 >= next)
            {
                next += 1000;
                smash.Run(stand, at, s, new ServerLimits());
                var w = smash.LastRun;
                if (i < 12 || i % 10 == 0) runs.Add($"{i}s: reach {w.InReach} looked {w.Looked} new {w.New} insp {w.InspectMicros}us{(w.Stopped ? " STOP" : "")}");
                i++;
            }
            else if (smash.Pending(stand))
            {
                smash.Resume(stand, at, s, new ServerLimits());
                resumes++;
            }
            await t.Ticks(1);
            var now = Environment.TickCount64 - start;
            foreach (var target in watched)
                if (!gone.ContainsKey(target.Entity) && !World.Exists(target.Entity)) gone[target.Entity] = now;
            if (gone.Count == watched.Count) break;
        }
        runs.Add($"{resumes} resumed steps");
        var at1 = World.Position(stand) ?? at0;
        foreach (var target in watched.OrderBy(w => w.Position.DistanceTo(at0)))
        {
            var e = target.Entity;
            var sight = world.Sights.TryGetValue(e, out var d) ? string.Join(",", d.Select(kv => $"{kv.Key}x{kv.Value}")) : "none";
            if (world.Blocked.TryGetValue(e, out var why)) sight += $" ({why})";
            var h = hits.TryGetValue(e, out var l) ? l : [];
            Info($"{label}: {Name(target.Template)} {e} dist {target.Position.DistanceTo(at0):0.0} dy {target.Position.Y - at0.Y:+0.0;-0.0} " +
                 $"hits {h.Count} [{string.Join(" ", h.Select(x => $"{x / 1000.0:0.0}"))}] sight [{sight}] " +
                 $"{(gone.TryGetValue(e, out var g) ? $"gone at {g / 1000.0:0.0} s" : "STILL STANDING")}");
        }
        var times = gone.Values.Order().ToList();
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).DefaultIfEmpty(0).Max();
        Info($"{label}: SUMMARY broke {gone.Count}/{watched.Count}; last at {(times.Count > 0 ? times[^1] / 1000.0 : 0):0.0} s; " +
             $"longest gap between breaks {gaps / 1000.0:0.0} s; hits {hits.Values.Sum(x => x.Count)} (+{otherHits} on other props); " +
             $"sight {string.Join(",", world.SightTotals.Select(kv => $"{kv.Key}x{kv.Value}"))} (not answered: {string.Join(",", world.Reasons.Select(kv => $"{kv.Key}x{kv.Value}"))}); inspects {world.InspectCalls}; stand-in moved {at1.DistanceTo(at0):0.0} m");
        Info($"{label}: runs {string.Join(" | ", runs)}");
    }

    private static readonly string[] RingTemplates =
    [
        "Prop_Destructible_Ancients_Barrel_01", "Prop_Destructible_Furniture_Barrel01_A_Wood", "Prop_Destructible_Fogger_Crate02_A",
        "Prop_Destructible_Ancients_Vase_Small_03_A", "Prop_Tall_Trees_Destructible_Barrel01_A",
    ];

    [KilnTest(TimeoutSeconds = 400, Order = 10)]
    public async Task Pace_spawned_room(TestContext t)
    {
        var (anchor, what) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var a = anchor!.Value;
        var table = Table();
        Assert.True(table.Count > 1000, "the smash table loaded");
        foreach (var visibleOnly in new[] { true, false })
        {
            var c = Ground(a.X + 70 + (visibleOnly ? 0 : 40), a.Y, a.Z + 50);
            var stand = await Spawn.Entity("1_Player", c, 0, new SpawnOptions { Tag = "labsmash" });
            var spawned = new List<EntityId>();
            // a ring of 12 props 2 to 7 m away
            for (var i = 0; i < 12; i++)
            {
                var ang = i * Math.PI * 2 / 12;
                var r = 2 + (i % 6);
                var p = Ground(c.X + Math.Cos(ang) * r, c.Y, c.Z + Math.Sin(ang) * r, 6);
                spawned.Add(await Spawn.Entity(RingTemplates[i % RingTemplates.Length], p, 0, new SpawnOptions { Tag = "labsmash" }));
            }
            // two stacks of three barrels, 4 m away
            foreach (var dx in new[] { -4.0, 4.0 })
                for (var level = 0; level < 3; level++)
                {
                    var g = Ground(c.X + dx, c.Y, c.Z + 3, 6);
                    spawned.Add(await Spawn.Entity("Prop_Destructible_Ancients_Barrel_01", new WorldPosition(g.X, g.Y + level * 1.05, g.Z), 0,
                        new SpawnOptions { Tag = "labsmash" }));
                }
            await t.Ticks(20);
            var world = new KilnSmashWorld();
            var watched = spawned.Select(e => world.Inspect(e)).OfType<SmashTarget>().ToList();
            await RunPace(t, $"spawned room visibleOnly={visibleOnly}", stand, watched, table,
                new PersonalSettings { Radius = 15, VisibleOnly = visibleOnly }, 75);
            foreach (var e in spawned) if (World.Exists(e)) Spawn.Destroy(e);
            if (World.Exists(stand)) Spawn.Destroy(stand);
        }
        Spawn.CleanupTracked("labsmash");
    }

    /// <summary>A room of stone built in the air (Kiln.Build), so line of sight answers and nothing of the world is in the
    /// way: a 16 x 16 m floor slab, walls round a 10 x 10 m room 3.2 m high, a ceiling and a shelf. Inside: a ring of
    /// barrels, crates and urns, two stacks of three barrels, barrels on the shelf. Outside the walls, on the same slab:
    /// four barrels that must stay whole while "Only what I can see" is on.</summary>
    [KilnTest(TimeoutSeconds = 600, Order = 12)]
    public async Task Pace_built_room(TestContext t)
    {
        Assert.True(Build.Available, $"kiln.build: {Build.Status.Detail}");
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var table = Table();
        var c = await FreshAir(anchor!.Value);
        var stone = (byte)155;
        const double hx = 5, hz = 5, wall = 0.5, ceiling = 3.2;
        var results = new List<string>();
        void Note(BuildResult r) => results.Add($"{(r.Ok ? "ok" : $"refused {r.Reason}")} {r.CellsChanged}");
        System.Numerics.Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);
        Note(await Build.Add(new BuildShape(ShapeKind.Box, new WorldPosition(c.X, c.Y - 0.5, c.Z), V(8, 0.5, 8)), stone));   // the slab
        foreach (var (dx, dz, sx, sz) in new[] { (hx + wall, 0.0, wall, hz + wall), (-hx - wall, 0.0, wall, hz + wall), (0.0, hz + wall, hx + wall, wall), (0.0, -hz - wall, hx + wall, wall) })
            Note(await Build.Add(new BuildShape(ShapeKind.Box, new WorldPosition(c.X + dx, c.Y + ceiling / 2, c.Z + dz), V(sx, ceiling / 2, sz)), stone));
        Note(await Build.Add(new BuildShape(ShapeKind.Box, new WorldPosition(c.X, c.Y + ceiling + 0.5, c.Z), V(hx + 2 * wall, 0.5, hz + 2 * wall)), stone));
        Note(await Build.Add(new BuildShape(ShapeKind.Box, new WorldPosition(c.X, c.Y + 0.5, c.Z + hz - 0.5), V(hx - 1, 0.5, 0.5)), stone));   // the shelf
        var floor = Queries.GroundBelow(new WorldPosition(c.X, c.Y + 2, c.Z - 1), 10);
        for (var w = 0; w < 900 && floor is not { Ok: true, Hit: not null }; w++)
        {
            await t.Ticks(1);
            floor = Queries.GroundBelow(new WorldPosition(c.X, c.Y + 2, c.Z - 1), 10);
        }
        Assert.True(floor is { Ok: true, Hit: not null }, $"the rays see the floor ({floor.Reason})");
        var y = floor.Hit!.Position.Y + 0.05;
        var shelf = Queries.GroundBelow(new WorldPosition(c.X, c.Y + 2, c.Z + hz - 0.5), 10);
        var shelfY = shelf is { Ok: true, Hit: { } sh } ? sh.Position.Y + 0.05 : y + 1;
        var stand = await Spawn.Entity("1_Player", new WorldPosition(c.X, y, c.Z - 1), 0, new SpawnOptions { Tag = "labsmash" });
        var inside = new List<EntityId>();
        var outside = new List<EntityId>();
        async Task Put(List<EntityId> into, string template, double x, double py, double z) =>
            into.Add(await Spawn.Entity(template, new WorldPosition(x, py, z), 0, new SpawnOptions { Tag = "labsmash" }));
        for (var i = 0; i < 8; i++)
        {
            var ang = i * Math.PI * 2 / 8;
            await Put(inside, RingTemplates[i % RingTemplates.Length], c.X + Math.Cos(ang) * 3.2, y, c.Z - 0.5 + Math.Sin(ang) * 3.2);
        }
        foreach (var dx in new[] { -hx + 0.8, hx - 0.8 })                         // stacks of three in two corners
            for (var level = 0; level < 3; level++)
                await Put(inside, "Prop_Destructible_Ancients_Barrel_01", c.X + dx, y + level * 1.05, c.Z - hz + 0.8);
        foreach (var dx in new[] { -2.5, 0.0, 2.5 })                             // on the shelf
            await Put(inside, "Prop_Destructible_Furniture_Barrel01_A_Wood", c.X + dx, shelfY, c.Z + hz - 0.5);
        foreach (var (dx, dz) in new[] { (hx + 2, 0.0), (-hx - 2, 0.0), (0.0, -hz - 2), (0.0, hz + 2) })   // behind the walls
            await Put(outside, "Prop_Destructible_Ancients_Barrel_01", c.X + dx, y, c.Z + dz);
        await t.Ticks(30);
        await t.Delay(TimeSpan.FromSeconds(5));   // the rays' own cell cache trails the build
        var probe = Queries.VoxelLineOfSight(stand, outside[0]);
        var probeIn = Queries.VoxelLineOfSight(stand, inside[0]);
        Info($"built room: slab, 4 walls, ceiling, shelf: {string.Join("; ", results)}; floor {y:0.00}, shelf {shelfY:0.00}; " +
             $"sight to a barrel behind the east wall: ok {probe.Ok} visible {probe.Visible} {probe.Reason}; to one inside: ok {probeIn.Ok} visible {probeIn.Visible} {probeIn.Reason}");
        var world = new KilnSmashWorld();
        var watched = inside.Concat(outside).Select(e => world.Inspect(e)).OfType<SmashTarget>().ToList();
        await RunPace(t, "built room visibleOnly=True", stand, watched, table, new PersonalSettings { Radius = 15 }, 30);
        Info($"built room: SUMMARY inside broken {inside.Count(e => !World.Exists(e))}/{inside.Count}, behind the walls broken {outside.Count(e => !World.Exists(e))}/{outside.Count}");
        foreach (var e in inside.Concat(outside)) if (World.Exists(e)) Spawn.Destroy(e);
        if (World.Exists(stand)) Spawn.Destroy(stand);
        Spawn.CleanupTracked("labsmash");
    }

    /// <summary>A fresh spot in the air for a built test: random, away from the anchor and from earlier runs' builds
    /// (the lab's save keeps them), 60 m above the anchor, with its tiles loaded.</summary>
    private static async Task<WorldPosition> FreshAir(WorldPosition a)
    {
        var r = new Random();
        var ang = r.NextDouble() * Math.PI * 2;
        var dist = 60 + r.NextDouble() * 140;
        var c = new WorldPosition(Math.Round(a.X + Math.Cos(ang) * dist), Math.Round(a.Y + 60), Math.Round(a.Z + Math.Sin(ang) * dist));
        await Build.Load(Build.BoxAround(c, 20, 20, 20), TimeSpan.FromSeconds(30));
        return c;
    }

    /// <summary>A stone block in the air, 14 x 6 x 14 m, its top the floor the stand-in stands on; inside it a sealed
    /// cellar (air from 4.5 to 1.5 m below the top) holding two layers of urns, every one out of sight and nearer than
    /// anything in view; 8 barrels in plain view on the top, 6.6 m away. Does the smash reach the barrels in view, and
    /// does it leave the urns?</summary>
    [KilnTest(TimeoutSeconds = 600, Order = 13)]
    public async Task Pace_cellar_below(TestContext t)
    {
        Assert.True(Build.Available, $"kiln.build: {Build.Status.Detail}");
        var (anchor, _) = FindAnchor();
        Assert.True(anchor is not null, "an anchor");
        var table = Table();
        var c = await FreshAir(anchor!.Value);
        var stone = (byte)155;
        var block = await Build.Add(new BuildShape(ShapeKind.Box, new WorldPosition(c.X, c.Y - 3, c.Z), new System.Numerics.Vector3(7, 3, 7)), stone);
        var dug = await Build.Dig(new BuildShape(ShapeKind.Box, new WorldPosition(c.X, c.Y - 3, c.Z), new System.Numerics.Vector3(5.5f, 1.5f, 5.5f)));
        var top = Queries.GroundBelow(new WorldPosition(c.X, c.Y + 5, c.Z), 20);
        var cellarFloor = Queries.GroundBelow(new WorldPosition(c.X, c.Y - 2, c.Z), 20);
        for (var w = 0; w < 900 && (top is not { Ok: true, Hit: not null } || cellarFloor is not { Ok: true, Hit: not null }); w++)
        {
            await t.Ticks(1);
            top = Queries.GroundBelow(new WorldPosition(c.X, c.Y + 5, c.Z), 20);
            cellarFloor = Queries.GroundBelow(new WorldPosition(c.X, c.Y - 2, c.Z), 20);
        }
        Assert.True(top is { Ok: true, Hit: not null } && cellarFloor is { Ok: true, Hit: not null }, $"the rays see the block ({top.Reason}, {cellarFloor.Reason})");
        var floor = top.Hit!.Position.Y;
        var under = cellarFloor.Hit!.Position.Y;
        var stand = await Spawn.Entity("1_Player", new WorldPosition(c.X, floor + 0.05, c.Z), 0, new SpawnOptions { Tag = "labsmash" });
        var hidden = new List<EntityId>();
        var seen = new List<EntityId>();
        foreach (var layer in new[] { 0.05, 0.65 })
            for (var ix = 0; ix < 11; ix++)
                for (var iz = 0; iz < 11; iz++)
                    hidden.Add(await Spawn.Entity("Prop_Destructible_Ancients_Vase_Small_03_A",
                        new WorldPosition(c.X - 3 + ix * 0.6, under + layer, c.Z - 3 + iz * 0.6), 0, new SpawnOptions { Tag = "labsmash" }));
        for (var i = 0; i < 8; i++)
        {
            var ang = i * Math.PI * 2 / 8 + 0.2;
            seen.Add(await Spawn.Entity("Prop_Destructible_Ancients_Barrel_01", new WorldPosition(c.X + Math.Cos(ang) * 6.6, floor + 0.05, c.Z + Math.Sin(ang) * 6.6), 0,
                new SpawnOptions { Tag = "labsmash" }));
        }
        await t.Ticks(30);
        var toHidden = Queries.VoxelLineOfSight(stand, hidden[60]);
        var toSeen = Queries.VoxelLineOfSight(stand, seen[0]);
        for (var w = 0; w < 600 && (!toHidden.Ok || !toSeen.Ok); w++)
        {
            await t.Ticks(1);
            toHidden = Queries.VoxelLineOfSight(stand, hidden[60]);
            toSeen = Queries.VoxelLineOfSight(stand, seen[0]);
        }
        await t.Delay(TimeSpan.FromSeconds(5));   // the rays' own cell cache trails the build (a NotLoaded answer turns sight off for 5 s)
        Info($"cellar: block {(block.Ok ? "ok" : $"refused {block.Reason}")} {block.CellsChanged}, dig {(dug.Ok ? "ok" : $"refused {dug.Reason}")} {dug.CellsChanged} cells; " +
             $"floor {floor:0.00}, cellar floor {under:0.00}; sight to an urn in the cellar: ok {toHidden.Ok} visible {toHidden.Visible} {toHidden.Reason}; " +
             $"to a barrel in view: ok {toSeen.Ok} visible {toSeen.Visible} {toSeen.Reason}");
        var world = new KilnSmashWorld();
        var watched = seen.Concat(hidden).Select(e => world.Inspect(e)).OfType<SmashTarget>().ToList();
        await RunPace(t, $"cellar below ({hidden.Count} urns out of sight, {seen.Count} barrels in view)", stand, watched, table, new PersonalSettings { Radius = 15 }, 40);
        Info($"cellar: SUMMARY barrels in view broken {seen.Count(e => !World.Exists(e))}/{seen.Count}, urns out of sight broken {hidden.Count(e => !World.Exists(e))}/{hidden.Count}");
        foreach (var e in seen.Concat(hidden)) if (World.Exists(e)) Spawn.Destroy(e);
        if (World.Exists(stand)) Spawn.Destroy(stand);
        Spawn.CleanupTracked("labsmash");
    }
}
