// SPDX-License-Identifier: MIT
// The probe build's console: commands from a text file, answers and traces in a log, so a developer can spawn things
// next to the player in their own hosted world and see what the loot loop and the smasher do with them. Only in the
// probe build (dotnet build -p:KilnAutoLootProbe=true), never in a release.
//
// Files: <dir>/cmd.txt (commands, one per line; only lines added after the world started run) and <dir>/out.log
// (answers, traces). <dir>: $KILN_AUTOLOOT_PROBE_DIR, else under Wine $HOME/.cache/kiln-auto-loot-probe (through Z:),
// else <the mod's data folder>/probe; kiln.log names it. What the probe spawns carries the game's DoNotSave (Spawn's
// Transient flag) unless "keep" is given, so it never reaches the save. "help" lists the commands.
#if KILN_AUTOLOOT_PROBE
using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;

// a developer's tool for a hosting game with every service on
#pragma warning disable KILN121

namespace KilnAutoLoot.Probe;

internal sealed class ProbeConsole
{
    private const string Tag = "kiln-auto-loot-probe";
    private const string StashChest = "Prop_Decoration_T5_Storage_40_Chest";   // the Silver Chest a player builds (40 slots)
    private const int StashChestSlots = 40;
    private static ProbeConsole? _current;

    private readonly LootServer _server;
    private readonly LootLoop _loop;
    private readonly Data _data;
    private readonly KilnGame _game;
    private readonly DiagLog _out;
    private readonly string _cmdPath;
    private readonly ConcurrentQueue<string> _commands = new();
    private volatile bool _stopped;

    private readonly Dictionary<uint, Watched> _watched = new();
    private readonly Dictionary<(uint, string), (string? Text, int Repeats)> _last = new();
    private Dictionary<uint, int>? _bag;
    private StashJob? _stash;

    /// <summary>"stash": the backpack's slots moved one by one into chests placed next to the player.</summary>
    private sealed class StashJob
    {
        public required EntityId Player { get; init; }
        public required List<EntityId> Chests { get; init; }
        public required Queue<SlotId> Slots { get; init; }
        public required long StartMs { get; init; }
        public HashSet<uint> Full { get; } = [];
        public Task<MoveResult>? Move { get; set; }
        public SlotId Moving { get; set; }
        public int Done { get; set; }
        public int Failed { get; set; }
        public string? LastError { get; set; }
    }
    private long _nextBagMs, _nextWatchMs;

    private sealed class Watched
    {
        public required EntityRef Ref { get; init; }
        public required string Name { get; init; }
        public required DateTime Since { get; init; }
        public bool Gone { get; set; }
    }

    private ProbeConsole(LootServer server, LootLoop loop, Data data, string dir)
    {
        _server = server;
        _loop = loop;
        _data = data;
        _cmdPath = Path.Combine(dir, "cmd.txt");
        _out = new DiagLog(Path.Combine(dir, "out.log"));
        _game = new KilnGame(Say);
        Directory.CreateDirectory(dir);
        if (!File.Exists(_cmdPath)) File.WriteAllText(_cmdPath, "");
        var start = new FileInfo(_cmdPath).Length;   // commands written before this world are not run
        new Thread(() => Read(start)) { IsBackground = true, Name = "kiln-auto-loot probe" }.Start();
    }

    // ---------------------------------------------------------------- hooks (ProbeTrace)

    public static void WorldUp(LootServer server, LootLoop loop, Data data, ModContext? context)
    {
        WorldDown();
        var dir = DirOf(context);
        try { _current = new ProbeConsole(server, loop, data, dir); }
        catch (Exception e)
        {
            KilnLog.Warn($"kiln-auto-loot probe: no console in {dir}: {e.Message}");
            return;
        }
        var c = _current;
        ProbeTrace.Watching = e => c._watched.TryGetValue(e.Value, out var w) && !w.Gone;
        ProbeTrace.Sink = c.Trace;
        KilnLog.Info($"kiln-auto-loot probe: commands from {c._cmdPath}, answers in {Path.Combine(dir, "out.log")}");
        c.Say($"probe ready: Kiln Auto Loot {AutoLootMod.Version}, game data {(data.Ready ? "read" : "NOT read yet")} " +
              $"({data.Templates.Count} loot rules, {data.Smash.Count} smash targets), hosting {Kiln.Client.IsHosting}. Type help.");
    }

    public static void Tick() => _current?.OnTick();

    public static void WorldDown()
    {
        ProbeTrace.Watching = null;
        ProbeTrace.Sink = null;
        if (_current is not { } c) return;
        _current = null;
        c._stopped = true;
        c.Say("world stops: probe off");
        c._out.Dispose();
    }

    private static string DirOf(ModContext? context)
    {
        var env = Environment.GetEnvironmentVariable("KILN_AUTOLOOT_PROBE_DIR");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (OperatingSystem.IsWindows())
        {
            // Wine passes the Linux environment on, and Z: is the Linux root
            var home = Environment.GetEnvironmentVariable("HOME");
            if (home is not { Length: > 1 } || home[0] != '/')
                home = Environment.GetEnvironmentVariable("WINEHOMEDIR")?.Replace(@"\??\", "") is { } w && w.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)
                    ? w[2..].Replace('\\', '/')
                    : null;
            if (home is { Length: > 1 } && home[0] == '/')
            {
                var z = "Z:" + home.Replace('/', '\\');
                if (Directory.Exists(z)) return Path.Combine(z, ".cache", "kiln-auto-loot-probe");
            }
        }
        return Path.Combine(context?.DataDirectory ?? ".", "probe");
    }

    // ---------------------------------------------------------------- the command file (reader thread)

    private void Read(long offset)
    {
        var pending = new StringBuilder();
        while (!_stopped)
        {
            try
            {
                var length = new FileInfo(_cmdPath) is { Exists: true } fi ? fi.Length : 0;
                if (length < offset) offset = 0;   // the file was emptied
                if (length > offset)
                {
                    using var f = new FileStream(_cmdPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    f.Seek(offset, SeekOrigin.Begin);
                    var buf = new byte[length - offset];
                    var n = f.Read(buf, 0, buf.Length);
                    offset += n;
                    pending.Append(Encoding.UTF8.GetString(buf, 0, n));
                    var text = pending.ToString();
                    var end = text.LastIndexOf('\n');
                    if (end >= 0)
                    {
                        foreach (var line in text[..end].Split('\n'))
                            if (line.Trim() is { Length: > 0 } cmd && !cmd.StartsWith('#')) _commands.Enqueue(cmd);
                        pending.Clear().Append(text[(end + 1)..]);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Thread.Sleep(200);
        }
    }

    // ---------------------------------------------------------------- tick thread

    private void OnTick()
    {
        while (_commands.TryDequeue(out var line)) Run(line);
        StashStep();
        var now = Environment.TickCount64;
        if (now >= _nextWatchMs)
        {
            _nextWatchMs = now + 250;
            CheckWatched();
        }
        if (now >= _nextBagMs)
        {
            _nextBagMs = now + 1000;
            CheckBag();
        }
    }

    private void Say(string text) => _out.Write(text);

    private void Run(string line)
    {
        Say($"> {line}");
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var args = parts[1..];
        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "help": Help(); break;
                case "where": Where(); break;
                case "spawn": SpawnCmd(args); break;
                case "item": ItemCmd(args); break;
                case "find": FindCmd(args, watch: false); break;
                case "near": FindCmd(["_", .. args], watch: false); break;
                case "watch": FindCmd(args, watch: true); break;
                case "unwatch":
                    _watched.Clear();
                    _last.Clear();
                    Say("watching nothing");
                    break;
                case "status": Status(); break;
                case "info": Info(args); break;
                case "accept": Accept(args); break;
                case "lootall": LootAll(args); break;
                case "destroy": Destroy(args); break;
                case "clear": Clear(); break;
                case "stash": Stash(); break;
                case "inv": Inv(args); break;
                case "settings": Say(SettingsJson.Write(_loop.SettingsOf(Host()?.Entity ?? default))); break;
                case "set": Set(args); break;
                case "counters": Counters(); break;
                case "templates": Templates(args); break;
                case "items": Items(args); break;
                default: Say($"unknown command '{parts[0]}' (help)"); break;
            }
        }
        catch (Exception e)
        {
            Say($"error: {e.GetType().Name}: {e.Message}");
        }
    }

    private void Help() => Say(string.Join("\n  ", new[]
    {
        "commands (one per line in cmd.txt; ids as 123 or #123):",
        "where                                   the player's position and facing",
        "spawn <template|part> [count] [dist] [up] [keep]   in front of the player (default 1, 4 m, 0 m); watched",
        "item <item id|name part> [amount] [dist]          a ground item in front of the player; watched",
        "find <name part> [radius]               entities near the player (default 30 m), with our rule and smash kind",
        "near [radius]                           every entity with '_' in its template name near the player",
        "watch <name part> [radius]              find + trace what the loop and the smasher decide for them",
        "unwatch | status                        stop watching | the watched entities now",
        "info <id>                               components, offer, slots, pickup, rule, smash kind of one entity",
        "accept <id> | lootall <id>              send the game's accept (E) | loot-all to an entity as the player",
        "destroy <id> | clear                    remove one entity | every entity the probe spawned (stash chests stay)",
        "stash                                   move every backpack slot into new silver chests next to the player (saved)",
        "inv [name part]                         the player's bag; bag changes are logged every second anyway",
        "settings | set <key> <value>            the player's effective settings | change one (server side only)",
        "counters                                the loop's counters",
        "templates <name part> | items <name part>   game templates with our rule and smash kind | items with ids",
    }));

    private Player? Host()
    {
        if (Kiln.Client.IsHosting && !Kiln.Client.LocalPlayer.IsNone && Kiln.Players.Find(Kiln.Client.LocalPlayer) is { } local) return local;
        foreach (var p in Kiln.Players.All)
            if (p.State == PlayerState.InGame)
                return p;
        return null;
    }

    private (Player Player, WorldPosition At, Vector3 Forward, float Yaw)? Pose()
    {
        if (Host() is not { Position: { } at } p)
        {
            Say("no player in game");
            return null;
        }
        var f = p.Aim?.Direction ?? new Vector3(0, 0, 1);
        f.Y = 0;
        f = f.LengthSquared() < 1e-6f ? new Vector3(0, 0, 1) : Vector3.Normalize(f);
        return (p, at, f, p.Aim?.YawDegrees ?? 0);
    }

    private static Vector3 Turn(Vector3 f, double degrees)
    {
        var r = degrees * Math.PI / 180;
        var (c, s) = (Math.Cos(r), Math.Sin(r));
        return new Vector3((float)(f.X * c - f.Z * s), 0, (float)(f.X * s + f.Z * c));
    }

    private static WorldPosition Ahead(WorldPosition at, Vector3 dir, double dist, double up) =>
        new(at.X + dir.X * dist, at.Y + up, at.Z + dir.Z * dist);

    private void Where()
    {
        if (Pose() is not { } pose) return;
        var p = pose.Player;
        Say($"player {p.Name} #{p.Entity.Value} at ({pose.At.X:0.0}, {pose.At.Y:0.0}, {pose.At.Z:0.0}), facing yaw {pose.Yaw:0}, " +
            $"in base {p.IsInBase}, in shroud {p.IsInShroud}, free slots {_game.FreeSlots(p.Entity)}");
    }

    // ---------------------------------------------------------------- spawning

    private void SpawnCmd(string[] a)
    {
        if (a.Length == 0)
        {
            Say("spawn <template|part> [count] [dist] [up] [keep]");
            return;
        }
        if (ResolveTemplate(a[0]) is not { } info || Pose() is not { } pose) return;
        var count = Math.Clamp(Int(a, 1, 1), 1, 20);
        var dist = Dbl(a, 2, 4);
        var up = Dbl(a, 3, 0);
        var keep = a.Contains("keep", StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var dir = Turn(pose.Forward, count == 1 ? 0 : (i - (count - 1) / 2.0) * 20);
            var pos = Ahead(pose.At, dir, dist, up);
            var r = Spawn.TryEntity(info.Id, pos, pose.Yaw + 180,
                new SpawnOptions { Tag = Tag, Flags = keep ? SpawnFlags.None : SpawnFlags.Transient });
            if (!r.Ok)
            {
                Say($"spawn {info.Name}: failed ({r.Reason}, code {r.Code})");
                continue;
            }
            Watch(r.Entity, info.Name);
            Say($"spawned #{r.Entity.Id.Value} {info.Name}, {dist:0.#} m ahead{(up != 0 ? $", {up:0.#} m up" : "")}" +
                $"{(keep ? ", kept in the save" : "")}: {Describe(info.Id)}");
        }
    }

    private void ItemCmd(string[] a)
    {
        if (a.Length == 0)
        {
            Say("item <item id|name part> [amount] [dist]");
            return;
        }
        if (ResolveItem(a[0]) is not { } row || Pose() is not { } pose) return;
        var amount = Math.Max(1, Int(a, 1, 1));
        var pos = Ahead(pose.At, pose.Forward, Dbl(a, 2, 3), 0.5);
        var r = Spawn.TryItem(new ItemId(row.Id), amount, pos, 0, new SpawnOptions { Tag = Tag, Flags = SpawnFlags.Transient });
        if (!r.Ok)
        {
            Say($"item {row.English}: failed ({r.Reason}, code {r.Code})");
            return;
        }
        var name = Spawn.Inspect(r.Entity.Id)?.TemplateName ?? row.Internal;
        Watch(r.Entity, name);
        Say($"spawned #{r.Entity.Id.Value} {name} with {amount} {row.English} ({row.Id})");
    }

    private TemplateInfo? ResolveTemplate(string q)
    {
        if (Guid.TryParse(q, out var g))
        {
            if (Spawn.Template(g) is { } byId) return byId;
            Say($"no template {g}");
            return null;
        }
        if (Spawn.Lookup(q) is { } exact) return exact;
        var hits = Spawn.Templates.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name.Length).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        if (hits.Count == 0)
        {
            Say($"no template has '{q}' in its name");
            return null;
        }
        if (hits.FirstOrDefault(t => t.Name.Equals(q, StringComparison.OrdinalIgnoreCase)) is { } same) return same;
        if (hits.Count > 1)
            Say($"'{q}': {hits.Count} templates, taking {hits[0].Name} (also: {string.Join(", ", hits.Skip(1).Take(8).Select(t => t.Name))}" +
                $"{(hits.Count > 9 ? ", ..." : "")})");
        return hits[0];
    }

    private ItemRow? ResolveItem(string q)
    {
        if (uint.TryParse(q, out var id))
        {
            if (_data.Items.TryGet(id, out var byId)) return byId;
            Say($"no item {id}");
            return null;
        }
        var hits = _data.Items.All.Where(r => r.Internal.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                              r.English.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.English.Length).ToList();
        if (hits.Count == 0)
        {
            Say($"no item has '{q}' in its name");
            return null;
        }
        if (hits.Count > 1)
            Say($"'{q}': {hits.Count} items, taking {hits[0].English} (also: {string.Join(", ", hits.Skip(1).Take(8).Select(r => r.English))})");
        return hits[0];
    }

    // ---------------------------------------------------------------- looking

    private void FindCmd(string[] a, bool watch)
    {
        if (a.Length == 0)
        {
            Say("find <name part> [radius]");
            return;
        }
        if (Pose() is not { } pose) return;
        var radius = Dbl(a, 1, a[0] == "_" ? 8 : 30);
        var found = Spawn.FindByName(a[0], pose.At, radius, 128)
            .Where(e => e != pose.Player.Entity)
            .Select(e => (Entity: e, Info: Spawn.Inspect(e), At: World.Position(e)))
            .OrderBy(x => x.At is { } p ? p.DistanceTo(pose.At) : double.MaxValue).ToList();
        Say($"{found.Count} entities with '{a[0]}' within {radius:0.#} m{(watch ? ", watching them" : "")}");
        foreach (var (e, info, at) in found)
        {
            var where = at is { } p ? $"{p.DistanceTo(pose.At):0.0} m, dy {p.Y - pose.At.Y:0.0}" : "no position";
            Say($"  #{e.Value} {info?.TemplateName ?? "?"} ({where}): {Describe(info?.Template)}");
            if (watch && info is not null) Watch(new EntityRef(e, info.Serial), info.TemplateName ?? "?");
        }
    }

    private void Watch(EntityRef r, string name)
    {
        _watched[r.Id.Value] = new Watched { Ref = r, Name = name, Since = DateTime.Now };
        foreach (var key in _last.Keys.Where(k => k.Item1 == r.Id.Value).ToList()) _last.Remove(key);
    }

    private void Status()
    {
        var pose = Pose();
        Say($"{_watched.Count} watched");
        foreach (var w in _watched.Values)
        {
            var at = World.Position(w.Ref.Id);
            var where = at is { } p && pose is { } q ? $"{p.DistanceTo(q.At):0.0} m, dy {p.Y - q.At.Y:0.0}" : "-";
            Say($"  #{w.Ref.Id.Value} {w.Name}: {(w.Gone || !Alive(w.Ref) ? "gone" : "here")} ({where}), since {(DateTime.Now - w.Since).TotalSeconds:0} s");
        }
    }

    private static readonly string[] Components =
    [
        "PickupItem", "Inventory", "DefaultInventory", "InventorySetup", "RandomLoot", "InteractionOfferRuntime",
        "InteractionToggle", "ServerToggleState", "CheckPlayerInRange", "DestroyOnLoot", "DestroyOnState", "Destructible",
        "Health", "MiningNode", "RandomLootOnDestroy", "SpawnEntityOnDestroy", "Gravity", "DoNotSave", "Actor",
    ];

    private void Info(string[] a)
    {
        if (EntityArg(a) is not { } e) return;
        var info = Spawn.Inspect(e);
        var pose = Pose();
        var at = World.Position(e);
        var where = at is { } p && pose is { } q ? $"{p.DistanceTo(q.At):0.0} m, dy {p.Y - q.At.Y:0.0}" : "-";
        Say($"#{e.Value} {info?.TemplateName ?? "?"} (serial {info?.Serial}, exists {World.Exists(e)}, {where}): {Describe(info?.Template)}");
        Say($"  components: {string.Join(", ", Components.Where(c => World.Has(e, c)))}");
        var offer = _game.OfferOf(e);
        Say($"  offer: present {offer.Present}, disabled {offer.Disabled}, id {offer.OfferId}");
        var slots = _game.Slots(e);
        Say($"  slots: {(slots is null ? "none (no readable inventory)" : $"[{string.Join(", ", slots.Select(s => $"{ItemName(s.Item)} x{s.Count}"))}]")}");
        if (_game.TryPickupItem(e, out var item))
            Say($"  pickup: {ItemName(item)}, {_game.PickupDetails(e)}");
        if (World.Has(e, "ServerToggleState") && World.ReadBytes(e, "ServerToggleState") is { } bytes)
            Say($"  ServerToggleState bytes: {Convert.ToHexString(bytes)}");
    }

    private void Accept(string[] a)
    {
        if (EntityArg(a) is not { } e || Host() is not { } p) return;
        var offer = _game.OfferOf(e);
        if (!offer.Present)
        {
            Say($"#{e.Value} has no offer");
            return;
        }
        _game.Interact(p.Entity, e, offer.OfferId);
        Say($"accept sent to #{e.Value} (offer {offer.OfferId}, disabled {offer.Disabled})");
    }

    private void LootAll(string[] a)
    {
        if (EntityArg(a) is not { } e || Host() is not { } p) return;
        _game.Loot(p.Entity, e);
        Say($"loot-all sent to #{e.Value}");
    }

    private void Destroy(string[] a)
    {
        if (EntityArg(a) is not { } e) return;
        Say($"destroy #{e.Value}: {Spawn.Destroy(e)}");
    }

    private void Clear()
    {
        var r = Spawn.CleanupTracked(Tag);
        _watched.Clear();
        _last.Clear();
        Say($"removed {r.Destroyed} spawned entities ({r.Missing} already gone, {r.Kept} kept)");
    }

    private void Inv(string[] a)
    {
        if (Host() is not { } p) return;
        if (_game.Bag(p.Entity) is not { } bag)
        {
            Say("the bag cannot be read");
            return;
        }
        var q = a.Length > 0 ? string.Join(' ', a) : "";
        var rows = Count(bag).Where(kv => q.Length == 0 || ItemName(kv.Key).Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => ItemName(kv.Key)).Take(60).ToList();
        Say($"bag: {bag.FreeSlots} free slots; {string.Join(", ", rows.Select(kv => $"{ItemName(kv.Key)} x{kv.Value}"))}");
    }

    private void Set(string[] a)
    {
        if (a.Length < 2 || Host() is not { } p)
        {
            Say("set <key> <value>, e.g. set stones off, set heightAbove 0");
            return;
        }
        var copy = SettingsJson.Read(SettingsJson.Write(_loop.SettingsOf(p.Entity)))!;
        var value = a[1].ToLowerInvariant() switch { "on" => "true", "off" => "false", _ => string.Join(' ', a[1..]) };
        Client.SettingValues.Set(copy, a[0], value);
        _server.SetLocalPersonal(p.Entity, copy);
        Say($"{a[0]} = {Client.SettingValues.Get(_loop.SettingsOf(p.Entity), a[0])} (server side only: the F8 window does not show " +
            "it, and a change there replaces it)");
    }

    private void Counters()
    {
        var c = _loop.Counters;
        Say(string.Join(", ", c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(long) || f.FieldType == typeof(int))
            .Select(f => $"{f.Name} {f.GetValue(c)}")));
    }

    private void Templates(string[] a)
    {
        if (a.Length == 0)
        {
            Say("templates <name part>");
            return;
        }
        var hits = Spawn.Templates.Where(t => t.Name.Contains(a[0], StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Name).ToList();
        Say($"{hits.Count} templates with '{a[0]}'{(hits.Count > 60 ? ", the first 60" : "")}");
        foreach (var t in hits.Take(60)) Say($"  {t.Name}: {Describe(t.Id)}");
    }

    private void Items(string[] a)
    {
        var q = a.Length > 0 ? string.Join(' ', a) : "";
        var hits = _data.Items.All.Where(r => r.Internal.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                              r.English.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
        Say(string.Join(", ", hits.Select(r => $"{r.English} = {r.Internal} ({r.Id})")));
    }

    private void Stash()
    {
        if (_stash is not null)
        {
            Say("a stash is still running");
            return;
        }
        if (Pose() is not { } pose) return;
        if (Kiln.Items.Read(pose.Player.Entity) is not { } inv)
        {
            Say("the bag cannot be read");
            return;
        }
        var slots = inv.Slots.Where(x => !x.IsEmpty && x.Count > 0 && x.Category == InventoryCategory.Generic && !x.IsEquipped)
            .Select(x => x.Slot).ToList();
        if (slots.Count == 0)
        {
            Say("the backpack is empty");
            return;
        }
        var chests = new List<EntityId>();
        var count = (slots.Count + StashChestSlots - 1) / StashChestSlots;
        for (var i = 0; i < count; i++)
        {
            var pos = Ahead(pose.At, Turn(pose.Forward, 40 + i * 35), 2.5, 0);
            // kept in the save, and not in Kiln's list of spawned things: a chest like one the player built
            var r = Spawn.TryEntity(StashChest, pos, pose.Yaw + 180, new SpawnOptions { Flags = SpawnFlags.NoTrack });
            if (!r.Ok)
            {
                Say($"stash: no chest ({r.Reason}, code {r.Code}): nothing moved");
                return;
            }
            chests.Add(r.Entity.Id);
        }
        _stash = new StashJob
        {
            Player = pose.Player.Entity,
            Chests = chests,
            Slots = new Queue<SlotId>(slots),
            StartMs = Environment.TickCount64,
        };
        Say($"stash: {slots.Count} backpack slots into {chests.Count} silver chest(s) next to the player " +
            $"({string.Join(", ", chests.Select(c => $"#{c.Value}"))}); moving one slot at a time");
    }

    private void StashStep()
    {
        if (_stash is not { } j) return;
        if (j.Move is { } m)
        {
            if (!m.IsCompleted) return;
            var status = m.IsCompletedSuccessfully ? m.Result.Status : ItemsStatus.Invalid;
            if (status == ItemsStatus.Done) j.Done++;
            else if (status is ItemsStatus.NoRoom or ItemsStatus.Partial && j.Chests.FirstOrDefault(c => !j.Full.Contains(c.Value)) is { IsNone: false } full)
            {
                j.Full.Add(full.Value);                                      // that chest is full: the slot goes to the next one
                j.Slots.Enqueue(j.Moving);
            }
            else
            {
                j.Failed++;
                j.LastError = m.IsCompletedSuccessfully ? status.ToString() : m.Exception?.GetBaseException().Message;
            }
            j.Move = null;
        }
        if (j.Slots.Count == 0)
        {
            Say($"stash done: {j.Done} slots moved, {j.Failed} failed{(j.LastError is { } e ? $" (last: {e})" : "")}");
            _stash = null;
            return;
        }
        var target = j.Chests.FirstOrDefault(c => !j.Full.Contains(c.Value) && Kiln.Items.Read(c) is not null);
        if (target.IsNone)
        {
            // a new chest gets its inventory a moment after it appears
            if (Environment.TickCount64 - j.StartMs < 10_000) return;
            Say($"stash stopped: no chest with room can be read; {j.Done} slots moved, {j.Slots.Count} left in the backpack");
            _stash = null;
            return;
        }
        j.Moving = j.Slots.Dequeue();
        j.Move = Kiln.Items.Move(j.Player, j.Moving, target);
    }

    // ---------------------------------------------------------------- watching

    private void Trace(EntityId e, string text)
    {
        var site = text.IndexOf(':') is var i and > 0 ? text[..i] : text;
        var key = (e.Value, site);
        _last.TryGetValue(key, out var last);
        if (last.Text == text)
        {
            _last[key] = (text, last.Repeats + 1);
            return;
        }
        if (last.Repeats > 0) Say($"#{e.Value} {NameOf(e)}: ({site}: the line before {last.Repeats} more times)");
        _last[key] = (text, 0);
        Say($"#{e.Value} {NameOf(e)}: {text}");
    }

    private string NameOf(EntityId e) => _watched.TryGetValue(e.Value, out var w) ? w.Name : "?";

    private static bool Alive(EntityRef r) => r.HasSerial ? r.IsAlive : Spawn.Exists(r.Id);

    private void CheckWatched()
    {
        foreach (var w in _watched.Values)
        {
            if (w.Gone || Alive(w.Ref)) continue;
            w.Gone = true;
            Say($"#{w.Ref.Id.Value} {w.Name}: GONE after {(DateTime.Now - w.Since).TotalSeconds:0.0} s");
        }
    }

    private void CheckBag()
    {
        if (Host() is not { } p || _game.Bag(p.Entity) is not { } read) return;
        var now = Count(read);
        if (_bag is { } before)
            foreach (var id in now.Keys.Union(before.Keys))
            {
                var d = now.GetValueOrDefault(id) - before.GetValueOrDefault(id);
                if (d != 0) Say($"bag: {d:+0;-0} {ItemName(id)} (now {now.GetValueOrDefault(id)})");
            }
        _bag = now;
    }

    private static Dictionary<uint, int> Count(BagRead read)
    {
        var d = new Dictionary<uint, int>();
        foreach (var s in read.Slots) d[s.Item] = d.GetValueOrDefault(s.Item) + s.Count;
        return d;
    }

    // ---------------------------------------------------------------- helpers

    private string Describe(Guid? template)
    {
        if (template is not { } t) return "no template";
        var rule = _data.Templates.TryGet(t, out var r)
            ? $"rule {r.Group} {r.Flags}{(r.Yield.Count > 0 ? $" giving {string.Join(", ", r.Yield.Take(3).Select(ItemName))}" : "")}"
            : "no loot rule";
        var smash = _data.Smash.TryGet(t, out var k) ? $"smash {k}" : "no smash";
        return $"{rule}; {smash}";
    }

    private string ItemName(uint id) => _data.Items.TryGet(id, out var row) && row.English.Length > 0 ? $"{row.English} ({id})" : id.ToString(CultureInfo.InvariantCulture);

    private EntityId? EntityArg(string[] a)
    {
        if (a.Length > 0 && uint.TryParse(a[0].TrimStart('#'), out var v)) return new EntityId(v);
        Say("which entity? give its id (from find, near or spawn)");
        return null;
    }

    private static int Int(string[] a, int i, int fallback) =>
        a.Length > i && int.TryParse(a[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static double Dbl(string[] a, int i, double fallback) =>
        a.Length > i && double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
#endif
