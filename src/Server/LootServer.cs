// SPDX-License-Identifier: MIT
// The server world's part: looting for every player. Runs only where a server world runs (Kiln.Client.IsDedicatedServer ||
// Kiln.Client.IsHosting), between ServerStartedEvent and ServerStoppingEvent, once per hosted world; every per-world and
// per-player state is dropped at the stop. This file is the Kiln glue (lifecycle, config, channel, events, ticks,
// diagnostics); the rules are in LootLoop, the Kiln calls of the loop in KilnGame.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;

namespace KilnAutoLoot.Server;

internal sealed class LootServer(Data data)
{
    private const string StatusId = "kiln-auto-loot.server";
    private const long CounterLineMs = 5000;   // the debug counter line every 5 s

    public Data Data { get; } = data;

    /// <summary>The server settings (kiln-auto-loot-server.json), registered at the first ServerStartedEvent and kept
    /// for the whole process (a config cannot be registered twice); null outside a running server world. In a hosted game
    /// the window edits it on the server thread and calls Save; the change comes back through Changed.</summary>
    public ModConfig<ServerSettings>? ServerConfig => _running ? _config : null;

    /// <summary>`enabled` as the running world started with it (the value in effect until the next world); null outside a
    /// running server world. Any thread.</summary>
    public bool? EnabledInWorld => _running ? _enabled : null;

    /// <summary>A notice for the hosting game's own player (never sent over the network). Tick thread.</summary>
    public event Action<EntityId, NoticeMsg>? LocalNotice;

    /// <summary>What the server applies for the hosting game's own player. Tick thread.</summary>
    public event Action<ServerInfoMsg>? LocalServerInfo;

    private readonly TickClock _clock = new();
    private readonly Smash _smash = new(data);
    private readonly List<EventSubscription> _subscriptions = [];
    private readonly HashSet<EntityId> _badJsonLogged = [];
    private ModConfig<ServerSettings>? _config;
    private ModContext? _context;
    private LootLoop? _loop;
    private DiagLog? _diag;
    private bool _started, _looting, _subscribedConfig;
    private volatile bool _running, _enabled;   // read by the window (EnabledInWorld)

    /// <summary>The capabilities the loop cannot work without. Items, combat and line of sight are
    /// optional: KilnGame falls back when they are off.</summary>
    private static readonly string[] Needed = ["kiln.players", "world.find", "kiln.spawn", "gameplay.emit", "gameplay.intercept"];

    private static string? MissingCapability()
    {
        foreach (var c in Needed)
            if (!Capabilities.Is(c)) return $"{c} is not available ({Capabilities.Why(c)})";
        return null;
    }
    private long _nextCounterLine, _lastCountedScans = -1, _nextErrorLog;
    private (EntityId Player, PersonalSettings Settings)? _hostPersonal;
    private ServerLimits? _limitsFor;   // the limits _limitsJson was written for
    private string? _limitsJson;
    private (EntityId Player, bool Open)? _hostWindow;

    private static bool IsServerWorld => Kiln.Client.IsDedicatedServer || Kiln.Client.IsHosting;

    private Logger? Log => _context?.Log;

    /// <summary>From OnLoad (tick thread): subscribes; the world part starts at ServerStartedEvent.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _context = AutoLootMod.ModContext;
        _subscriptions.Add(Events.On<ServerStartedEvent>(e => StartWorld($"ServerStarted ({e.Source})")));
        _subscriptions.Add(Events.On<ServerStoppingEvent>(e => StopWorld($"ServerStopping ({e.Reason})")));
        _subscriptions.Add(Events.On<LootEvent>(OnLoot));
        _subscriptions.Add(Events.On<ItemPickupEvent>(OnItemPickup));
        AutoLootMod.Hub.PersonalReceived += OnPersonal;
        AutoLootMod.Hub.ClientStateReceived += OnClientState;
        AutoLootMod.Hub.CommandReceived += OnCommand;
        if (_context is not null) _context.Tick.Pre += OnTick;
        if (IsServerWorld && Lifecycle.IsServerRunning) StartWorld("loaded while the server runs");
    }

    public void Stop()
    {
        if (!_started) return;
        StopWorld("the mod unloads");
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
        AutoLootMod.Hub.PersonalReceived -= OnPersonal;
        AutoLootMod.Hub.ClientStateReceived -= OnClientState;
        AutoLootMod.Hub.CommandReceived -= OnCommand;
        if (_context is not null) _context.Tick.Pre -= OnTick;
        _diag?.Dispose();
        _diag = null;
        _started = false;
    }

    /// <summary>The local player's settings, from the client part in a hosted game (same process). Tick thread. No skip
    /// ids: the server part reads the same Data as the hosting game, whose item table has the game's English and German
    /// names, so the skip list matches here as it does in the window.</summary>
    public void SetLocalPersonal(EntityId player, PersonalSettings settings)
    {
        _hostPersonal = (player, settings.Clone());
        if (_loop is null) return;   // applied when the world starts
        _loop.SetPersonal(player, settings.Clone());
        SettingsLine(player, "host");
        SendInfo(player);
    }

    /// <summary>The host's own command (the gather focus key), from the client part in the same process. Tick thread.
    /// Answers with LocalServerInfo.</summary>
    public void SetLocalCommand(EntityId player, Net.CommandKind kind) => RunCommand(player, kind);

    /// <summary>The local player's window state, from the client part in a hosted game. Tick thread.</summary>
    public void SetLocalWindowOpen(EntityId player, bool open)
    {
        _hostWindow = (player, open);
        _loop?.SetWindowOpen(player, open);
    }

    // ---------------------------------------------------------------- world lifecycle

    private void StartWorld(string why)
    {
        if (_running || !IsServerWorld) return;
        _config ??= Config.Register<ServerSettings>(ConfigKind.Server);
        if (!_subscribedConfig)
        {
            _config.Changed += OnConfigChanged;
            _subscribedConfig = true;
        }
        var s = _config.Value;
        _enabled = s.Enabled;   // the file's value now; kept for this world, a change applies to the next one
        _running = true;
        _clock.Advance(0);
        if (_context is not null) _diag ??= new DiagLog(Path.Combine(_context.DataDirectory, "kiln-auto-loot.log"));
        _loop = new LootLoop(new KilnGame(KeyLine), _clock, Data, SendNotice, KeyLine);
        _loop.Configure(s);
        _nextCounterLine = _clock.NowMs + CounterLineMs;
        _lastCountedScans = -1;

        var d = _loop.SettingsOf(default);
        KeyLine($"Kiln Auto Loot {AutoLootMod.Version}: world up ({why}); enabled={s.Enabled} " +
                $"maxStack={s.MaxStack} backpackCheat={s.BackpackCheat} counterLog={s.CounterLog} maxRadius={s.Limits.MaxRadius}");
        KeyLine($"defaults for players without the mod: radius={d.Radius:0.0} looting={B(d.Looting)} corpses={B(d.Corpses)} " +
                $"takeInPlace={B(d.TakeInPlace)} visibleOnly={B(d.VisibleOnly)}");
        KeyLine($"data: {Data.Templates.Count} templates, {Data.Items.All.Count} items, {Data.Quest.Count} quest items");
        ProbeTrace.WorldUp(this, _loop, Data, _context);   // the probe build only
        if (!_enabled)
        {
            KeyLine("enabled=false in kiln-auto-loot-server.json: this world gets no looting, smashing or data changes");
            return;
        }
        WorldData.Apply(s);
        _smash.Start();
        _looting = MissingCapability() is null;
        if (!_looting)
        {
            // fail safe: a needed service is off: nothing automatic, the game keeps running
            KeyLine($"looting is off in this world: {MissingCapability()}");
            return;
        }
#pragma warning disable KILN121 // kiln.players is in Needed, checked above
        Kiln.Players.Joined += OnJoined;
        Kiln.Players.Left += OnLeft;
        foreach (var p in Kiln.Players.All) _loop.Join(p.Entity);
#pragma warning restore KILN121
        if (_hostPersonal is { } lp) _loop.SetPersonal(lp.Player, lp.Settings.Clone());
        if (_hostWindow is { } lw) _loop.SetWindowOpen(lw.Player, lw.Open);
        foreach (var p in _loop.Players.Keys) SendInfo(p);
        try { StatusLines.Add(Status, StatusId); }
        catch (Exception e) { Log?.Warn($"Kiln Auto Loot: no status line ({e.Message})"); }
    }

    private void StopWorld(string why)
    {
        if (!_running) return;
        _running = false;
        if (_looting)
        {
#pragma warning disable KILN121 // subscribed only when kiln.players was on
            Kiln.Players.Joined -= OnJoined;
            Kiln.Players.Left -= OnLeft;
#pragma warning restore KILN121
        }
        _looting = false;
        if (_enabled)
        {
            _smash.Stop();
            WorldData.Reset();
        }
        try { StatusLines.Remove(StatusId); }
        catch (Exception) { }
        ProbeTrace.WorldDown();   // the probe build only
        if (_loop is not null) KeyLine($"world stops ({why}): {_loop.Counters.Summary()}");
        _loop?.Reset();
        _loop = null;
        _limitsFor = null;
        _badJsonLogged.Clear();
    }

    private void OnConfigChanged(ConfigChangedEvent e)
    {
        if (!_running || _loop is null || _config is null) return;
        var s = _config.Value;
        _limitsFor = null;   // the limits may have changed in place: write their JSON again
        if (s.Enabled != _enabled) KeyLine($"enabled={s.Enabled}: takes effect when the world starts again");
        if (!_enabled) return;
        WorldData.Apply(s);
        _loop.Configure(s);
        foreach (var p in _loop.Players.Keys) SendInfo(p);
    }

    // ---------------------------------------------------------------- players and messages

    private void OnJoined(Player p)
    {
        if (!_running || !_looting || _loop is null) return;
        _loop.Join(p.Entity);
        if (IsHostPlayer(p.Entity))
        {
            if (_hostPersonal is { } lp && lp.Player == p.Entity) _loop.SetPersonal(lp.Player, lp.Settings.Clone());
            SendInfo(p.Entity);
        }
    }

    private void OnLeft(Player p)
    {
        _badJsonLogged.Remove(p.Entity);
        if (_loop is null) return;
        _loop.Leave(p.Entity);
        if (_enabled) _smash.PlayerLeft(p.Entity);
    }

    /// <summary>Only kept here: the tick applies the latest one per player (ApplySettings), so a flood of messages costs
    /// one application per player per half second.</summary>
    private void OnPersonal(PersonalMsg m, EntityId player)
    {
        if (!_running || _loop is null) return;
        // the ids come from the player's own item names (English and German); this server may know internal names only
        _loop.QueuePersonal(player, m.Json, m.SkipIds, m.OnlyIds);
    }

    /// <summary>A player's command (the gather focus key). Tick thread.</summary>
    private void OnCommand(CommandMsg m, EntityId player) => RunCommand(player, m.Kind);

    /// <summary>Every command is answered with the player's ServerInfoMsg (FocusItem = the focus after it), also when
    /// nothing changed, so the window always shows the server's state.</summary>
    private void RunCommand(EntityId player, CommandKind kind)
    {
        if (!_running || _loop is null) return;
        if (_loop.Command(player, kind, apply: _enabled && _looting).Reply) SendInfo(player);
    }

    private void ApplySettings()
    {
        if (_loop is null) return;
        foreach (var (player, read) in _loop.ApplyQueuedSettings())
        {
            if (!read && _badJsonLogged.Add(player))
                Log?.Warn($"Kiln Auto Loot: player {player} sent settings that do not read; keeping the previous ones");
            SettingsLine(player, "sent");
            SendInfo(player);
        }
    }

    private void OnClientState(ClientStateMsg m, EntityId player) => _loop?.SetWindowOpen(player, m.WindowOpen);

    private static bool IsHostPlayer(EntityId player) => Kiln.Client.IsHosting && Kiln.Client.HasLocalPlayer && Kiln.Client.LocalPlayer == player;

    private void SendNotice(EntityId player, NoticeMsg m)
    {
        if (IsHostPlayer(player)) LocalNotice?.Invoke(player, m);
        else if (ChannelHub.HasMod(player)) AutoLootMod.Hub.SendTo(player, m);
    }

    private void SendInfo(EntityId player)
    {
        if (_loop is null || _config is null) return;
        var s = _config.Value;
        if (!ReferenceEquals(_limitsFor, s.Limits))
        {
            _limitsFor = s.Limits;
            _limitsJson = SettingsJson.WriteLimits(s.Limits);
        }
        var msg = new ServerInfoMsg(_loop.EffectiveJsonOf(player), _limitsJson!,
            s.MaxStack, s.BackpackCheat, Kiln.Client.IsHosting, _enabled, LineOfSight: _loop.SightAvailable,
            FocusItem: _loop.FocusOf(player));
        if (IsHostPlayer(player)) LocalServerInfo?.Invoke(msg);
        else if (ChannelHub.HasMod(player)) AutoLootMod.Hub.SendTo(player, msg);
    }

    // ---------------------------------------------------------------- tick, takes by hand, smash, diagnostics

    private void OnTick(in TickInfo tick)
    {
        _clock.Advance(tick.WallDeltaMs);
        if (!_running || _loop is null) return;
        ProbeTrace.Tick();   // the probe build only
        try { ApplySettings(); }   // also in a world with the mod off: the players' windows get the answer
        catch (Exception e)
        {
            if (_clock.NowMs >= _nextErrorLog)
            {
                _nextErrorLog = _clock.NowMs + 60_000;
                Log?.Error(e, "Kiln Auto Loot: applying a player's settings failed");
            }
        }
        if (!_enabled) return;
        try
        {
            if (_looting) _loop.Step();
            SmashStep();
            CounterLine();
        }
        catch (Exception e)
        {
            // never let the loop fault the tick; say it at most once a minute
            if (_clock.NowMs >= _nextErrorLog)
            {
                _nextErrorLog = _clock.NowMs + 60_000;
                Log?.Error(e, "Kiln Auto Loot: the loot loop failed this step");
            }
        }
    }

    /// <summary>The player's own LootEvent (E pressed or held): never canceled, whatever the player's switches say (a
    /// single press of E and the hold-E sweep look the same here, and what a player takes by hand is theirs to take).
    /// Noted for gather focus only. Pre, tick thread.</summary>
    private void OnLoot(LootEvent e)
    {
        if (!_running || !_enabled || !_looting || _loop is null || e.Injected || !e.ByPlayer || e.Canceled) return;
        try
        {
            _loop.HandLoot(e.Guest, e.Host, e.LootAll);   // a single pickup by hand: confirmed when the item arrives
        }
        catch (Exception ex)
        {
            if (_clock.NowMs >= _nextErrorLog)
            {
                _nextErrorLog = _clock.NowMs + 60_000;
                Log?.Error(ex, "Kiln Auto Loot: noting a take by hand failed");
            }
        }
    }

    /// <summary>Items arrived in a player's inventory: confirms a pending hand take (gather focus). Tick thread.</summary>
    private void OnItemPickup(ItemPickupEvent e)
    {
        if (!_running || !_looting || _loop is null) return;
        _loop.ItemReceived(e.Receiver, e.ItemId);
    }

    /// <summary>Smash.Run once per second per player (the smash rhythm).</summary>
    private void SmashStep()
    {
        if (_loop is null || _config is null) return;
        var now = _clock.NowMs;
        foreach (var (entity, state) in _loop.Players)
        {
            if (now < state.NextSmashMs || !_loop.Acts(entity)) continue;   // nothing before their own settings
            state.NextSmashMs = now + 1000;
            if (Kiln.Players.Find(entity) is not { State: PlayerState.InGame, IsDead: false, Position: { } at }) continue;
            _smash.Hit = _config.Value.CounterLog ? (t, kind, sw) => SmashLine(entity, t, kind, sw) : null;
            _smash.Run(entity, at, state.Settings, _config.Value.Limits);
        }
    }

    /// <summary>counterLog: one line per smash hit, with the switches the server used for that player.</summary>
    private void SmashLine(EntityId player, SmashTarget t, SmashKind kind, SmashSwitches sw) =>
        _diag?.Write($"smash: player {player} hit {Spawn.Template(t.Template)?.Name ?? t.Template.ToString()} as " +
                     $"{SmashTable.WordOf(kind)} (stones {B(sw.Stones)}, buildables {B(sw.Buildables)}, trees {B(sw.Trees)}, hives {B(sw.Hives)})");

    /// <summary>counterLog: the switches a player's settings came with, as the server applies them.</summary>
    private void SettingsLine(EntityId player, string how)
    {
        if (_config?.Value.CounterLog != true || _loop is null) return;
        var s = _loop.SettingsOf(player);
        _diag?.Write($"settings: player {player} ({how}): looting {B(s.Looting)}, groundLoot {B(s.GroundLoot)}, stones {B(s.Stones)}, " +
                     $"collectibles {B(s.Collectibles)}, plants {B(s.Plants)}, crops {B(s.Crops)}, fish {B(s.Fish)}, " +
                     $"corpses {B(s.Corpses)}, bosses {B(s.Bosses)}, chests {B(s.Chests)}, " +
                     $"smash {B(s.Smash)}, buildables {B(s.SmashBuildables)}, fellTrees {B(s.FellTrees)}, hives {B(s.SmashHives)}, radius {s.Radius:0} " +
                     $"(ground {s.GroundRadius:0}, collectibles {s.CollectiblesRadius:0}, plants {s.PlantsRadius:0}, " +
                     $"crops {s.CropsRadius:0}, corpses {s.CorpsesRadius:0}, chests {s.ChestsRadius:0}), " +
                     $"height {s.HeightAbove:0}/{s.HeightBelow:0}, filter {s.ItemFilter}, skip \"{Short(s.SkipList)}\", " +
                     $"only \"{Short(s.OnlyList)}\"");
    }

    private static string Short(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= 80 ? text : text[..80] + "...";

    private void CounterLine()
    {
        if (_loop is null || _config?.Value.CounterLog != true || _clock.NowMs < _nextCounterLine) return;
        _nextCounterLine = _clock.NowMs + CounterLineMs;
        var c = _loop.Counters;
        if (c.PlayerScans == _lastCountedScans) return;   // only when something was scanned since the last line
        _lastCountedScans = c.PlayerScans;
        _diag?.Write(c.CountersLine());
        _diag?.Write(c.TimingLine());
    }

    private void KeyLine(string text)
    {
        Log?.Info(text);
        _diag?.Write(text);
    }

    private string? Status() =>
        _loop is null ? null : $"Kiln Auto Loot: {_loop.Players.Count} player(s); {_loop.Counters.Summary()}";

    private static int B(bool b) => b ? 1 : 0;
}
