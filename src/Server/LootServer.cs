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

    /// <summary>The server settings while this player's game runs no world (the main menu): the window edits the file
    /// there, and the next world this game hosts starts with it. A player asked to set the stack size before loading a
    /// save, not only inside it. Null in a world, and until <see cref="EnsureMenuConfig"/> registered the
    /// file.</summary>
    public ModConfig<ServerSettings>? MenuConfig => !_running && !Kiln.Client.InGame ? _config : null;

    /// <summary>`enabled` as the running world started with it (the value in effect until the next world); null outside a
    /// running server world. Any thread.</summary>
    public bool? EnabledInWorld => _running ? _enabled : null;

    /// <summary>A notice for the hosting game's own player (never sent over the network). Tick thread.</summary>
    public event Action<EntityId, NoticeMsg>? LocalNotice;

    /// <summary>What the server applies for the hosting game's own player. Tick thread.</summary>
    public event Action<ServerInfoMsg>? LocalServerInfo;

    /// <summary>The salvage state for the hosting game's own player. Tick thread.</summary>
    public event Action<SalvageStateMsg>? LocalSalvageState;

    private SalvageServer? _salvage;

    private readonly TickClock _clock = new();
    private readonly Smash _smash = new(data);
    private readonly List<EventSubscription> _subscriptions = [];
    private readonly HashSet<EntityId> _badJsonLogged = [];
    private ModConfig<ServerSettings>? _config;
    private ModContext? _context;
    private LootLoop? _loop;
    internal Client.SightRayOverlay? SightRays { get; set; }
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

    private CommandRegistration? _command;   // "/autoloot near"

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
        _subscriptions.Add(Events.On<ItemTossEvent>(e => Salvage(s => s.Tossed(e))));
        // a player opening a chest's screen: gear taken out of world loot by hand comes with no pickup note
        _subscriptions.Add(Events.On<InteractEvent>(e =>
        {
            if (_enabled && e.ByPlayer && !e.Canceled && !e.Injected) Salvage(s => s.Opened(e.Guest, e.Host));
        }));
        try { _subscriptions.Add(Events.On<ItemPickingUpEvent>(e => Salvage(s => s.Looted(e.Receiver, e.GroundEntity)))); }
        catch (Exception e) { Log?.Info($"Kiln Auto Loot: no walk-over pickup events ({e.Message}); salvage rules see loot interactions only"); }
        AutoLootMod.Hub.SalvageCmdReceived += OnSalvageCmd;
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
        AutoLootMod.Hub.SalvageCmdReceived -= OnSalvageCmd;
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

    /// <summary>The host's own salvage command, from the client part in the same process. Tick thread.</summary>
    public void SetLocalSalvage(EntityId player, SalvageCmd kind, ulong[] tokens)
    {
        if (_salvage is null) LocalSalvageState?.Invoke(NoSalvage);
        else Salvage(s => s.Command(player, kind, tokens));
    }

    /// <summary>The answer while this world has no salvage (the mod off, looting off for a missing service).</summary>
    private static readonly SalvageStateMsg NoSalvage = new(SalvageReady.Off, 0, 0, [], default, false);

    private void OnSalvageCmd(SalvageCmdMsg m, EntityId player)
    {
        if (!_running) return;
        if (_salvage is null) AutoLootMod.Hub.SendTo(player, NoSalvage);
        else Salvage(s => s.Command(player, m.Kind, m.Tokens));
    }

    /// <summary>Runs salvage work; a failure is logged at most once a minute and never faults the tick.</summary>
    private void Salvage(Action<SalvageServer> work)
    {
        if (_salvage is not { } s) return;
        try { work(s); }
        catch (Exception e)
        {
            if (_clock.NowMs >= _nextSalvageError)
            {
                _nextSalvageError = _clock.NowMs + 60_000;
                Log?.Error(e, "Kiln Auto Loot: salvage failed this step");
            }
        }
    }

    private long _nextSalvageError;

    private void SendSalvage(EntityId player, SalvageStateMsg m)
    {
        if (IsHostPlayer(player)) LocalSalvageState?.Invoke(m);
        else if (ChannelHub.HasMod(player)) AutoLootMod.Hub.SendTo(player, m);
    }

    /// <summary>The local player's window state, from the client part in a hosted game. Tick thread.</summary>
    public void SetLocalWindowOpen(EntityId player, bool open)
    {
        _hostWindow = (player, open);
        _loop?.SetWindowOpen(player, open);
    }

    // ---------------------------------------------------------------- world lifecycle

    /// <summary>The window opened outside a world in a player's game: registers the server settings so the window can
    /// edit them (once per process; a world start registers them otherwise). Tick thread.</summary>
    public void EnsureMenuConfig()
    {
        if (!Kiln.Client.IsDedicatedServer && !Kiln.Client.InGame) RegisterConfig();
    }

    private ModConfig<ServerSettings> RegisterConfig()
    {
        if (_config is null)
        {
            // the defaults for players without the mod: keys added since the file was written start from the old ones
            var carry = ConfigCarryOver.Before(AutoLootMod.Id + "-server", "defaults");
            _config = Config.Register<ServerSettings>(ConfigKind.Server);
            ConfigCarryOver.After(_config, carry, line => AutoLootMod.ModContext?.Log.Info(line));
        }
        if (!_subscribedConfig)
        {
            _config.Changed += OnConfigChanged;   // ignored while no world runs
            _subscribedConfig = true;
        }
        return _config;
    }

    private void StartWorld(string why)
    {
        if (_running || !IsServerWorld) return;
        var s = RegisterConfig().Value;
        _enabled = s.Enabled;   // the file's value now; kept for this world, a change applies to the next one
        _running = true;
        // "/autoloot near": what every entity in the player's reach is, and what the next scan decides for it (0.2.8);
        // only where a world runs (a dedicated server, a hosting game)
        try { _command ??= Commands.Register("autoloot", NearCommand, CommandPermission.Everyone, usage: "near",
            description: "Kiln Auto Loot: what is around you and what Auto Loot does with each (also in kiln.log)"); }
        catch (Exception e) when (e is KilnUnavailableException or InvalidOperationException or ArgumentException)
        {
            Log?.Info($"Kiln Auto Loot: no /autoloot command ({e.Message})");
        }
        _clock.Advance(0);
        if (_context is not null) _diag ??= new DiagLog(Path.Combine(_context.DataDirectory, "kiln-auto-loot.log"));
        var game = new KilnGame(KeyLine) { SightRays = SightRays };
        _loop = new LootLoop(game, _clock, Data, SendNotice, KeyLine);
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
        SightRays?.SetHosted(true);
#pragma warning disable KILN121 // kiln.players is in Needed, checked above
        Kiln.Players.Joined += OnJoined;
        Kiln.Players.Left += OnLeft;
        foreach (var p in Kiln.Players.All) _loop.Join(p.Entity);
#pragma warning restore KILN121
        if (_hostPersonal is { } lp) _loop.SetPersonal(lp.Player, lp.Settings.Clone());
        if (_hostWindow is { } lw) _loop.SetWindowOpen(lw.Player, lw.Open);
        var loop = _loop;
        _salvage = new SalvageServer(Data, _clock, p => loop.SettingsOf(p), () => _config?.Value.Limits, SendSalvage, KeyLine);
        Salvage(s => s.StartWorld());
        loop.Moved = (p, from, items) => Salvage(s => s.MovedIn(p, from, items));
        foreach (var p in _loop.Players.Keys) SendInfo(p);
        try { StatusLines.Add(Status, StatusId); }
        catch (Exception e) { Log?.Warn($"Kiln Auto Loot: no status line ({e.Message})"); }
    }

    private void StopWorld(string why)
    {
        if (!_running) return;
        _running = false;
        SightRays?.SetHosted(false);
        _command?.Dispose();
        _command = null;
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
        _salvage?.StopWorld();
        _salvage = null;
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
        Salvage(s => s.Left(p.Entity));
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
            Salvage(s => s.Tick());
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
        if (!_running || !_enabled || !_looting || _loop is null || !e.ByPlayer || e.Canceled) return;
        Salvage(s => s.Looted(e.Guest, e.Host));   // the player's own and Auto Loot's: gear from it may meet a rule
        if (e.Injected) return;
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
        if (_enabled) Salvage(s => s.Arrived(e));
    }

    // SmashStep's players in this step, in turn order (kept to spare a list a step)
    private readonly List<(EntityId Entity, PlayerLoot State)> _smashOrder = [];

    /// <summary>A new Smash pass once per second per player (the smash rhythm); a pass that did not finish in its server
    /// step goes on in the next ones (Smash.Resume) until the next pass is due. All players' smashing shares one step's
    /// time and rays (Smash.BeginStep), and the players take turns going first.</summary>
    private void SmashStep()
    {
        if (_loop is null || _config is null) return;
        var now = _clock.NowMs;
        _smashOrder.Clear();
        foreach (var (entity, state) in _loop.Players) _smashOrder.Add((entity, state));
        var first = _smash.BeginStep(_smashOrder.Count);
        for (var i = 0; i < _smashOrder.Count; i++)
        {
            var (entity, state) = _smashOrder[(first + i) % _smashOrder.Count];
            var due = now >= state.NextSmashMs;
            if (!due && !_smash.Pending(entity)) continue;
            if (!_loop.Acts(entity)) continue;   // nothing before their own settings
            if (due) state.NextSmashMs = now + 1000;
            if (Kiln.Players.Find(entity) is not { State: PlayerState.InGame, IsDead: false, Position: { } at })
            {
                _smash.PlayerLeft(entity);   // dead or not in the game: no unfinished pass carries on
                continue;
            }
            _smash.Hit = _config.Value.CounterLog ? (t, kind, sw) => SmashLine(entity, t, kind, sw) : null;
            var started = LootLoop.StopwatchMicros();
            if (due) _smash.Run(entity, at, state.Settings, _config.Value.Limits);
            else _smash.Resume(entity, at, state.Settings, _config.Value.Limits);
            var spent = LootLoop.StopwatchMicros() - started;
            if (spent > LootLoop.SlowStepMicros && _loop.SlowLineDue(now))
            {
                var w = _smash.LastRun;
                KeyLine($"slow smash: {LootLoop.Ms(spent)} ms in one server step for player {entity}: {w.InReach} candidates in reach, " +
                        $"{w.Looked} looked at, {w.New} new (Spawn.Inspect {LootLoop.Ms(w.InspectMicros)} ms, slowest call " +
                        $"{LootLoop.Ms(w.SlowestInspect)} ms){(w.Stopped ? "; the rest goes on in the next server step" : "")}");
            }
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
        _diag?.Write($"settings: player {player} ({how}): looting {B(s.Looting)}, groundLoot {B(s.GroundLoot)}, stones {B(s.Stones)}, oldBooks {B(s.OldBooks)}, " +
                     $"collectibles {B(s.Collectibles)}, plants {B(s.Plants)}, critters {B(s.Critters)}, crops {B(s.Crops)}, " +
                     $"cropsInBase {B(s.CropsInBase)}, animalProduce {B(s.AnimalProduce)}, fish {B(s.Fish)}, " +
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

    /// <summary>"/autoloot near" (tick thread): arms a NearReport for the player's surroundings; the next whole scan
    /// answers in the player's chat and in kiln.log.</summary>
    private void NearCommand(CommandContext ctx)
    {
        if (ctx.Count == 0 || !string.Equals(ctx.Arg(0), "near", StringComparison.OrdinalIgnoreCase))
            ctx.Fail("usage: /autoloot near");
        if (!_running || _loop is null || !_enabled || !_looting)
            ctx.Fail("Kiln Auto Loot does not loot in this world now (no world, enabled=false, or a Kiln service is missing; see kiln.log)");
        var player = ctx.Source.Player;
        if (player.IsNone || Kiln.Players.Find(player) is not { Position: { } at })
            ctx.Fail("run it as a player, in chat: it looks around you");
        else
        {
            var s = _loop!.SettingsOf(player);
            var reach = Math.Max(LootRules.SearchRadius(s), s.EnemyPause ? s.EnemyDistance : 0);
            IReadOnlyList<EntityId> others = [];
            if (Capabilities.Is("queries.overlap_origins") && Queries.EntitiesInSphere(at, reach, player) is { Ok: true } r)
                others = r.Entities;
            _loop.Explain(NearReport.Around(_loop.Game, player, at, reach, others, (log, chat) =>
            {
                foreach (var line in log) KeyLine(line);
                foreach (var line in chat) Chat.Tell(player, line);
            }));
            ctx.Reply("Kiln Auto Loot: looking at what is around you in the next scan; the answer follows.");
        }
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
