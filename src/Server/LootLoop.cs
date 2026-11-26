// SPDX-License-Identifier: MIT
// Kiln Auto Loot's loot loop on the server, per player. Every game call goes through IGame (tests/Offline/Server fakes
// it). Tick thread only.
//
// One scan = every player once, every 100 ms by wall clock. Each scan evaluates every target in range
// afresh (at least every 2 s, so a corpse that was empty when it died is looked at again): nothing is ever "done" except through the
// visit table. At most 64 injects (accept, loot-all, Items.Move) per scan for the whole mod; the rest waits.
// On the server a scan spends at most StepBudgetMicros of one server step: what is left (a place full of targets the
// mod has never looked at, after a fast travel) goes on in the next steps, and the next scan starts once it is done.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;

namespace KilnAutoLoot.Server;

/// <summary>A command's outcome: the focus after it, and whether to answer with a ServerInfoMsg.</summary>
internal readonly record struct CommandResult(uint Focus, bool Reply);

internal sealed class LootLoop
{
    /// <summary>A slot move of the loop brought these item entities (gear) from this container into this player's bag:
    /// they arrive with no pickup event, so salvage rules learn of them here. Tick thread.</summary>
    public Action<EntityId, EntityId, IReadOnlyList<EntityId>>? Moved { get; set; }

    public const long ScanEveryMs = 100;           // each player every 100 ms
    public const int MaxInjectsPerScan = 64;       // for the whole mod
    public const long BlinkMs = 2000;              // the 2 s re-evaluation (counted only)
    public const long SightCacheMs = 1000;         // about 1 s per target
    public const long SightRetryMs = 5000;         // line of sight did not answer: the height window stands in this long
    public const long MoveRetryMs = 1000;          // a failed/partial/no-op move: fresh source read after a short delay
    public const long MovePendingMs = 2000;        // a result that has not arrived: one diagnostic, never requeue it
    public const int MaxMoveLines = 200;           // result/pending lines per world, then one cap line
    public const long StallRetryMs = 1000;         // a container the full player cannot take from
    public const float StaticPickupNearSquared = 0.5625f;   // the game's own 0.75 m, squared
    public const long SettingsGapMs = 500;         // one settings application per player per 500 ms
    public const int SettingsPerTick = 2;          // and at most two per tick for all players; the rest wait a tick
    public const int CommandsPerSecond = 4;        // gather focus commands per player per second; the rest are dropped
    public const long HandTakeWindowMs = 2000;     // a hand take counts when its item arrives within this time
    public const long StepBudgetMicros = 4_000;    // a scan's work per server step; the rest goes on in the next steps
    public const long SlowStepMicros = 50_000;     // a step over this leaves a "slow scan" line in kiln.log ...
    public const long SlowLineGapMs = 60_000;      // ... at most one a minute
    public const long LivestockEveryMs = 5_000;    // a farm animal's inventory is read once per 5 s (it refills in minutes)
    public const int LivestockMemory = 512;        // farm animals remembered per player before the stale ones are dropped

    // Candidate components, one World.Find each per scan, shared by all players.
    public const string PickupComponent = "PickupItem";
    public const string OfferComponent = "InteractionOfferRuntime";
    public const string InventoryComponent = "Inventory";
    public const string LootMarkerComponent = "DisableColliderModeChanges";
    public const string EnemyComponent = "EnemySettings";
    /// <summary>A fishing spot (the fish shoal the rod is cast at): looked for every <see cref="BlinkMs"/>, only for
    /// players with Fish on, for its kiln.log line.</summary>
    public const string FishingSpotComponent = "FishingSpot";

    private readonly IGame _game;
    private readonly IClock _clock;
    private readonly Data _data;
    private readonly Action<EntityId, NoticeMsg> _notice;
    private readonly Action<string> _keyLine;
    private readonly Dictionary<EntityId, PlayerLoot> _players = new();
    // per entity: what Spawn.Inspect said (its identity for the keys, the rule, the template) and its SpawnTime, which
    // tells a new entity under a reused id from the one before
    private readonly Dictionary<EntityId, (ulong Serial, TemplateRule? Rule, long Scan, Guid? Template, long Stamp)> _templates = new();
    private readonly CritterWatch _critters = new();
    private readonly CritterWatch _fish = new("fish");
    // what a corpse, chest or creature pile kept and why: one kiln.log line per target (a report of corpses "never
    // looted" was corpses keeping skipped stacks, which the game removes only once they are empty)
    private readonly CritterWatch _left = new("loot", "\"left\"");
    // corpses in reach the loop does not look into, and why (height window, radius, sight, a switch): one line per corpse
    private readonly CritterWatch _unread = new("loot", "\"not looked into\"");
    private NearReport? _near;   // "/autoloot near": the notes of the next whole scan for one player's surroundings
    private readonly HashSet<EntityId> _spotsSaid = [];
    private bool _spotsDue;   // this scan looks for fishing spots (every BlinkMs)
    private readonly HashSet<EntityId> _playerIds = [];
    private readonly Dictionary<string, IgnoreList> _ignoreCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignoreReported = new(StringComparer.Ordinal);
    private int _ignoreVersion = -1;
    private ServerSettings _server = new();
    private PersonalSettings _defaults = new();
    private long _lastScan = long.MinValue, _lastBlink, _scanNumber;
    private int _budget, _rotate;
    private long _sightUnknownUntil;   // line of sight answered "not known" (not loaded yet): the height window decides until then
    private readonly Func<long> _micros;
    private readonly CandidateIndex _index;   // this scan's candidate lists and shared positions
    // the scan in progress: every (player, target) to look at, and how far it got; Spot: a fishing spot for its kiln.log
    // line (FishingSpot), else a target
    private readonly List<(PlayerLoot P, GamePlayer Gp, EntityId E, bool Spot)> _work = [];
    private int _workAt, _workSightRetries;
    private bool _retrySightNextStep;
    private bool _scanOpen;
    private IEnumerator<bool>? _preparing;
    private IReadOnlyList<GamePlayer> _livePlayers = [];
    private long _scanMicros;
    private int _scanSteps;
    private StepWork _step;
    private long _stepEnd = long.MaxValue;
    private long? _lastSlowLine;
    private readonly HashSet<(EntityId Player, ulong Key, ulong Slot, string State)> _moveNotes = [];
    private int _moveLines;
    private bool _moveCapSaid;

    /// <summary>What one server step of the scan did (the "slow scan" line).</summary>
    private struct StepWork
    {
        public long StartMicros, EnemyMicros, TargetMicros, InspectMicros, SlowestInspect, FindMicros, ReadMicros, SlowestFind, SlowestRead, PlayerMicros;
        public int Players, Finds, Candidates, Reads, InReach, Targets, New;
        public EntityId SlowestEntity;
    }

    public LootCounters Counters { get; } = new();

    /// <param name="notice">sends a notice to a player's game (or hands it to the hosting game's client part).</param>
    /// <param name="keyLine">key log lines: unknown ignore entries.</param>
    /// <param name="micros">the work clock the step budget reads, in microseconds (tests: a fake one); default the
    /// Stopwatch.</param>
    public LootLoop(IGame game, IClock clock, Data data, Action<EntityId, NoticeMsg> notice, Action<string> keyLine,
        Func<long>? micros = null)
    {
        _micros = micros ?? StopwatchMicros;
        _index = new CandidateIndex(game, _micros);
        _game = game;
        _clock = clock;
        _data = data;
        _notice = notice;
        _keyLine = keyLine;
    }

    public IReadOnlyDictionary<EntityId, PlayerLoot> Players => _players;

    /// <summary>How long a player seen in game waits for their own settings before the world's defaults apply (a game
    /// without the mod). Until then the mod neither loots nor smashes for them: with the defaults it would take and break
    /// what they switched off (Stones on: boulders) in the moment before their settings arrive, longer on a slow PC. The
    /// wait starts when they are in the world, not at the join, so a long load does not use it up.</summary>
    public const long SettingsWaitMs = 10_000;

    /// <summary>Whether the mod acts for <paramref name="player"/> now: their own settings arrived, or they have been in
    /// game <see cref="SettingsWaitMs"/> without them. False for a player no scan has seen in game yet. Tick thread.</summary>
    public bool Acts(EntityId player) => _players.TryGetValue(player, out var p) && Settled(p, _clock.NowMs);

    private bool Settled(PlayerLoot p, long now)
    {
        if (p.OwnSettings) return true;
        if (p.FirstSeenMs is not { } seen || now - seen < SettingsWaitMs) return false;
        if (!p.DefaultsSaid)
        {
            p.DefaultsSaid = true;
            _keyLine($"player {p.Entity}: no settings from their game {SettingsWaitMs / 1000} s after they came into the " +
                     "world: the world's defaults apply (a game without Kiln Auto Loot)");
        }
        return true;
    }

    // ---------------------------------------------------------------- settings and players

    /// <summary>The server settings (at start and on every change): re-clamps every player.</summary>
    public void Configure(ServerSettings server)
    {
        _server = server;
        _defaults = SettingsRules.Clamp(server.Defaults, server.Limits);
        _defaultsJson = null;
        foreach (var p in _players.Values) Recompute(p);
    }

    public PlayerLoot Join(EntityId player)
    {
        if (_players.TryGetValue(player, out var p)) return p;
        p = new PlayerLoot(player, _clock);
        _players[player] = p;
        Recompute(p);
        return p;
    }

    /// <summary>The player left: every state of theirs goes, the gather focus with it.</summary>
    public void Leave(EntityId player) => _players.Remove(player);

    /// <summary>A player's own settings (PersonalMsg, or the host's window). Null = keep the previous ones (bad JSON).
    /// <paramref name="skipIds"/>: the item ids the player's game matched the skip list to (PersonalMsg.SkipIds), added
    /// to what the server matches itself. Returns the settings in effect after the server's limits.</summary>
    public PersonalSettings SetPersonal(EntityId player, PersonalSettings? sent, uint[]? skipIds = null, uint[]? onlyIds = null)
    {
        var p = Join(player);
        if (sent is not null && (!SettingsRules.ListsWithin(sent) || !SettingsRules.FitsMessage(sent)))
        {
            // over the bounds: the previous settings stay, whole
            if (!p.OverBoundsLogged)
            {
                p.OverBoundsLogged = true;
                _keyLine($"player {player} sent a skip list over {SkipListBounds.MaxEntries} entries or " +
                         $"{SkipListBounds.MaxEntryLength} characters per entry (or an only list or carry limits over those " +
                         $"bounds, or settings over {Channels.MaxJson} bytes as JSON); keeping the previous settings");
            }
            sent = null;
        }
        p.OwnSettings = true;
        if (sent is not null)
        {
            p.Sent = sent;
            p.SentSkipIds = skipIds;
            p.SentOnlyIds = onlyIds;
        }
        p.ReportsWindow = true;
        Recompute(p);
        return p.Settings;
    }

    /// <summary>A settings message from a player's game: kept as the player's latest, applied by
    /// <see cref="ApplyQueuedSettings"/> (a newer one before that replaces it).</summary>
    public void QueuePersonal(EntityId player, string json, uint[]? skipIds, uint[]? onlyIds = null) =>
        Join(player).PendingSettings = (json ?? "", skipIds ?? [], onlyIds ?? []);

    /// <summary>On the tick: applies each player's latest settings message when the last one was applied at least
    /// <see cref="SettingsGapMs"/> ago, so a game that sends many costs one application per gap, and at most
    /// <see cref="SettingsPerTick"/> per tick. Returns the players
    /// applied, with whether their JSON read (false = the previous settings stay).</summary>
    public List<(EntityId Player, bool Read)> ApplyQueuedSettings()
    {
        var applied = new List<(EntityId, bool)>();
        var now = _clock.NowMs;
        foreach (var p in _players.Values)
        {
            if (applied.Count >= SettingsPerTick) break;
            if (p.PendingSettings is not { } pending) continue;
            if (p.SettingsAppliedAt is { } at && now - at < SettingsGapMs) continue;
            p.PendingSettings = null;
            p.SettingsAppliedAt = now;
            var sent = SettingsJson.Read(pending.Json);
            SetPersonal(p.Entity, sent, pending.SkipIds, pending.OnlyIds);
            applied.Add((p.Entity, sent is not null));
        }
        return applied;
    }

    public void SetWindowOpen(EntityId player, bool open)
    {
        var p = Join(player);
        p.ReportsWindow = true;
        p.WindowOpen = open;
    }

    public PersonalSettings SettingsOf(EntityId player) => _players.TryGetValue(player, out var p) ? p.Settings : _defaults;

    /// <summary>The player's gather focus item, 0 = off (or not a player here).</summary>
    public uint FocusOf(EntityId player) => _players.TryGetValue(player, out var p) ? p.Focus : 0;

    /// <summary>The game layer the loop reads ("/autoloot near").</summary>
    internal IGame Game => _game;

    /// <summary>The server can check line of sight (visibleOnly does something).</summary>
    public bool SightAvailable => _game.SightAvailable;

    /// <summary>The effective settings as JSON, written once per change (ServerInfoMsg.EffectiveJson).</summary>
    public string EffectiveJsonOf(EntityId player) =>
        _players.TryGetValue(player, out var p) && p.EffectiveJson is { } json ? json : _defaultsJson ??= SettingsJson.Write(_defaults);

    private string? _defaultsJson;

    /// <summary>A gather focus command. ToggleFocus: focus on the item the player last took by hand, or off when focus is
    /// on; with no item taken by hand yet it stays off. ClearFocus: off. <paramref name="apply"/> false (the mod is off
    /// in this world): the focus does not change, the command is only answered.
    /// At most <see cref="CommandsPerSecond"/> per player per second count; the rest are dropped without an answer.
    /// Reply: a change is always answered; a command that changes nothing is answered once, then not again until
    /// something changes (the player's game already has that state).</summary>
    public CommandResult Command(EntityId player, CommandKind kind, bool apply = true)
    {
        var p = Join(player);
        var now = _clock.NowMs;
        if (p.CommandWindowStart is not { } start || now - start >= 1000)
        {
            p.CommandWindowStart = now;
            p.CommandCount = 0;
        }
        if (++p.CommandCount > CommandsPerSecond)
        {
            Counters.CommandsDropped++;
            return new CommandResult(p.Focus, Reply: false);
        }
        var focus = !apply ? p.Focus : kind switch
        {
            CommandKind.ToggleFocus => p.Focus != 0 ? 0u : p.HandItem,
            _ => 0u,
        };
        if (focus != p.Focus)
        {
            p.Focus = focus;
            RebuildFilter(p);
            if (focus != 0) Counters.FocusOn++;
            p.QuietAnswered = false;
            return new CommandResult(focus, Reply: true);
        }
        if (p.QuietAnswered) return new CommandResult(focus, Reply: false);
        p.QuietAnswered = true;
        return new CommandResult(focus, Reply: true);
    }

    /// <summary>The player's own loot request (not injected, not canceled by the game or another mod): a single
    /// pickup is remembered as pending; it becomes the hand item only when the same item arrives in the player's
    /// inventory (<see cref="ItemReceived"/>) within <see cref="HandTakeWindowMs"/>. A refused take (a full bag, a zone,
    /// another mod) never arrives and changes nothing.</summary>
    public void HandLoot(EntityId guest, EntityId host, bool lootAll)
    {
        if (lootAll || !_players.TryGetValue(guest, out var p) || HandPickupItem(host) is not { } item) return;
        p.HandPending = (item, _clock.NowMs + HandTakeWindowMs);
    }

    /// <summary>Items arrived in an inventory (the game's ItemPickupEvent): confirms a pending hand take of that item.
    /// The mod's own takes leave nothing pending, so they never set the hand item.</summary>
    public void ItemReceived(EntityId receiver, uint item)
    {
        if (!_players.TryGetValue(receiver, out var p) || p.HandPending is not { } pending) return;
        if (_clock.NowMs > pending.Until)
        {
            p.HandPending = null;
            return;
        }
        if (pending.Item != item) return;
        p.HandItem = item;
        p.HandPending = null;
    }

    /// <summary>The item of a ground pickup the player is about to take by hand (read before the game acts); null when
    /// the target is no pickup or goes down the container path. A critter's is the bait it gives.</summary>
    public uint? HandPickupItem(EntityId host)
    {
        var has = _game.TryPickupItem(host, out var item);
        var rule = Classify(host)?.Rule;
        if (rule is { Group: Group.Critter })
            return item != 0 ? item : CritterItem(rule) is var bait and not 0 ? bait : null;   // the bait it gives
        if (!has) return null;
        if (rule is not null && !Release.IsPickupPath(rule)) return null;
        return item;
    }

    /// <summary>The bait a critter gives (the first item of its default inventory), 0 = none known.</summary>
    private static uint CritterItem(TemplateRule rule) => rule.Yield.Count > 0 ? rule.Yield[0] : 0;

    /// <summary>Sets the hand item directly (tests).</summary>
    internal void TookByHand(EntityId player, uint item)
    {
        if (item != 0 && _players.TryGetValue(player, out var p)) p.HandItem = item;
    }

    /// <summary>At the world's stop: every per-world and per-player state goes (gather focus, command budgets, pending
    /// hand takes with it).</summary>
    public void Reset()
    {
        _players.Clear();
        _templates.Clear();
        _critters.Reset();
        _fish.Reset();
        _left.Reset();
        _moveNotes.Clear();
        _moveLines = 0;
        _moveCapSaid = false;
        _unread.Reset();
        _spotsSaid.Clear();
        if (_near is { } near) StopNear(near);
        _lastScan = long.MinValue;
        _work.Clear();
        _workAt = _workSightRetries = 0;
        _scanOpen = false;
        _preparing?.Dispose();
        _preparing = null;
        _livePlayers = [];
        _index.Reset();
    }

    internal static long StopwatchMicros() =>
        (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1_000_000.0 / System.Diagnostics.Stopwatch.Frequency));

    private void Recompute(PlayerLoot p)
    {
        p.Settings = p.Sent is null ? _defaults : SettingsRules.Clamp(p.Sent, _server.Limits);
        p.EffectiveJson = SettingsJson.Write(p.Settings);
        p.QuietAnswered = false;
        // a player's game that sent ids reports its own unknown entries: the server's names are not the player's
        var fromGame = p.Sent is not null ? p.SentSkipIds : null;
        var list = IgnoreOf(p.Settings.SkipList, report: fromGame is null);
        p.Ignore = fromGame is { Length: > 0 } ids ? list.WithIds(ids) : list;
        var onlyFromGame = p.Sent is not null ? p.SentOnlyIds : null;
        var only = IgnoreOf(p.Settings.OnlyList, report: false);
        p.Only = onlyFromGame is { Length: > 0 } oids ? only.WithIds(oids) : only;
        p.Limits = string.IsNullOrWhiteSpace(p.Settings.CarryLimits) ? CarryLimits.None : LimitsOf(p.Settings.CarryLimits);
        RebuildFilter(p);
    }

    private static void RebuildFilter(PlayerLoot p) =>
        p.Filter = new ItemFilter(p.Settings.ItemFilter, p.Ignore!, p.Only, p.Focus);

    private readonly Dictionary<string, CarryLimits> _limitsCache = new(StringComparer.Ordinal);
    private int _limitsVersion = -1;

    /// <summary>CarryLimits per distinct raw value, rebuilt when the item table is filled again.</summary>
    private CarryLimits LimitsOf(string raw)
    {
        if (_limitsVersion != _data.Items.Version || _limitsCache.Count > 64)
        {
            _limitsCache.Clear();
            _limitsVersion = _data.Items.Version;
        }
        if (!_limitsCache.TryGetValue(raw, out var limits)) _limitsCache[raw] = limits = new CarryLimits(raw, _data.Items);
        return limits;
    }

    /// <summary>IgnoreList per distinct raw value (players with the server's defaults share one), matched on the names
    /// this process has (on a dedicated server internal names, and English where Kiln's catalog gives it); rebuilt when
    /// the setting changes or the item table is filled again. <paramref name="report"/>: log the entries that match no
    /// item (once per raw value and table).</summary>
    private IgnoreList IgnoreOf(string raw, bool report)
    {
        raw ??= "";
        if (_ignoreVersion != _data.Items.Version)
        {
            _ignoreCache.Clear();
            _ignoreReported.Clear();
            _ignoreVersion = _data.Items.Version;
        }
        if (!_ignoreCache.TryGetValue(raw, out var list))
        {
            list = new IgnoreList(raw, _data.Items);
            if (_ignoreCache.Count > 64)
            {
                _ignoreCache.Clear();
                _ignoreReported.Clear();
            }
            _ignoreCache[raw] = list;
        }
        if (report && list.UnknownEntries.Count > 0 && _ignoreReported.Add(raw))
        {
            var text = string.Join(", ", list.UnknownEntries);
            if (text.Length > 240) text = text[..240] + "...";
            _keyLine($"skip list: {list.UnknownEntries.Count} entries match no known item: {text}");
        }
        return list;
    }

    // ---------------------------------------------------------------- the scan

    /// <summary>Every server step: goes on with the scan in progress, within <see cref="StepBudgetMicros"/>; starts the
    /// next scan when 100 ms passed since the last one started and it is done. A step slower than
    /// <see cref="SlowStepMicros"/> leaves one "slow scan" key line (at most one per <see cref="SlowLineGapMs"/>).</summary>
    public void Step()
    {
        var now = _clock.NowMs;
        if (!_scanOpen)
        {
            if (_lastScan != long.MinValue && now - _lastScan < ScanEveryMs) return;
            _lastScan = now;
        }
        var started = _micros();
        _step = default;
        if (!_scanOpen) Begin();
        Continue(started, StepBudgetMicros);
        var spent = _micros() - started;
        if (spent > SlowStepMicros) SlowLine(spent, now);
    }

    /// <summary>One whole scan of every player at once, without the step budget (tests, and a caller that wants it
    /// done now): the same incremental discovery and target decisions, run to completion. A scan in progress is finished
    /// first.</summary>
    public void Scan()
    {
        var started = _micros();
        _step = default;
        if (_scanOpen) Continue(started, long.MaxValue);
        _lastScan = _clock.NowMs;
        started = _micros();
        _step = default;
        Begin();
        Continue(started, long.MaxValue);
    }

    /// <summary>Starts a scan. Discovery, ordering and enemy checks are resumed by Continue, not done here.</summary>
    private void Begin()
    {
        var now = _clock.NowMs;
        _scanNumber++;
        _scanOpen = true;
        _scanMicros = 0;
        _scanSteps = 0;
        _work.Clear();
        _workAt = _workSightRetries = 0;
        _budget = MaxInjectsPerScan;
        Counters.Scans++;
        if (_near is { Scan: 0 } near) near.Scan = _scanNumber;
        _spotsDue = now - _lastBlink >= BlinkMs;
        if (_spotsDue)
        {
            _lastBlink = now;
            Counters.Rechecks++;
        }
        _preparing = Prepare().GetEnumerator();
    }

    private IEnumerable<bool> Prepare()
    {
        if (_ignoreVersion != _data.Items.Version)
            foreach (var player in _players.Values.ToArray())
            {
                Recompute(player);
                yield return true;
            }
        // Snapshot the players for discovery. Continue refreshes live state each step before using prepared work.
        var players = _livePlayers;
        _playerIds.Clear();
        foreach (var gp in players)
        {
            _playerIds.Add(gp.Entity);
            Join(gp.Entity);
            yield return true;
        }
        _index.Begin(players.Select(gp => (gp.Position, Reach(Join(gp.Entity).Settings))));
        _step.Players = players.Count;
        // Lazy per-player discovery below avoids searching at all while everybody is paused/off.
        for (var i = 0; i < players.Count; i++)
        {
            var gp = players[(i + _rotate) % players.Count];
            foreach (var turn in ScanPlayer(Join(gp.Entity), gp)) yield return turn;
        }
        if (players.Count > 0) _rotate = (_rotate + 1) % players.Count;
    }

    /// <summary>At most one indivisible operation beyond the deadline, including preparation. No minimum target
    /// after expensive preparation: the first target can wait for the next step.</summary>
    private void Continue(long stepStart, long budget)
    {
        _scanSteps++;
        _stepEnd = budget == long.MaxValue ? long.MaxValue : stepStart + budget;
        _index.Step();
        var t0 = _micros();
        _livePlayers = _game.Players();
        _step.PlayerMicros = _micros() - t0;
        _step.Players = _livePlayers.Count;
        _step.StartMicros += _step.PlayerMicros;
        var turns = 0;
        while (_preparing is not null && (turns == 0 || _micros() - stepStart < budget))
        {
            t0 = _micros();
            var inspectBefore = _step.InspectMicros;
            var enemyBefore = _step.EnemyMicros;
            var more = _preparing.MoveNext();
            _step.StartMicros += _micros() - t0 - (_step.InspectMicros - inspectBefore) - (_step.EnemyMicros - enemyBefore);
            turns++;
            if (!more)
            {
                _preparing.Dispose();
                _preparing = null;
            }
        }
        _step.Finds = _index.Finds;
        _step.FindMicros = _index.FindMicros;
        _step.Candidates = _index.Entities;
        _step.Reads = _index.Reads;
        _step.ReadMicros = _index.ReadMicros;
        _step.SlowestFind = _index.SlowestFind;
        _step.SlowestRead = _index.SlowestRead;
        _step.InReach = _work.Count;
        var targetsStart = _micros();
        var inspected = _step.InspectMicros;
        while (_preparing is null && _workAt < _work.Count && _micros() - stepStart < budget)
        {
            var (p, _, e, spot) = _work[_workAt++];
            var sightRetries = _workSightRetries;
            _workSightRetries = 0;
            _retrySightNextStep = false;
            if (!_players.TryGetValue(p.Entity, out var current) || !ReferenceEquals(current, p)) continue;
            var gp = _livePlayers.FirstOrDefault(g => g.Entity == p.Entity);
            if (gp.Entity.IsNone)
            {
                ProbeTrace.Note(p.Entity, "player: no longer alive and in the world");
                continue;
            }
            if (!Ready(p, gp)) continue;
            _step.Targets++;
            if (spot) FishingSpot(p, gp, e);
            else Target(p, gp, e);
            if (_retrySightNextStep && budget != long.MaxValue && sightRetries < 2)
            {
                // Up to two next-step continuations finish the three actual ray stages even with one ray per step.
                // A service that never has budget cannot pin the whole scan indefinitely: later work then gets a turn.
                _workAt--;
                _workSightRetries = sightRetries + 1;
                break;
            }
        }
        _step.TargetMicros = _micros() - targetsStart - (_step.InspectMicros - inspected);
        _scanMicros += _micros() - stepStart;
        if (_preparing is not null || _workAt < _work.Count)
        {
            foreach (var gp in _livePlayers)
                ProbeTrace.Note(gp.Entity, $"player: scan paused at its time budget ({(_preparing is not null ? "candidate discovery/order or enemy checks" : "targets")}); resumes next server step");
            return;
        }

        _scanOpen = false;
        _work.Clear();
        _workAt = _workSightRetries = 0;
        if (_near is { } report && report.Scan == _scanNumber) FinishNear(report);
        ForgetUnseenTemplates();
        Counters.ScanMicros += _scanMicros;
        Counters.ScanCount++;
        if (_scanMicros > Counters.ScanMaxMicros) Counters.ScanMaxMicros = _scanMicros;
        if (_scanSteps > Counters.ScanStepsMax) Counters.ScanStepsMax = _scanSteps;
    }

    /// <summary>The "slow scan" key line: the step's time, the phase that took most of it, and the counts.</summary>
    private void SlowLine(long spent, long now)
    {
        if (!SlowLineDue(now)) return;
        var w = _step;
        var phases = new (long Micros, string Name)[]
        {
            (w.StartMicros, "candidate discovery/order (Players, World.Find, positions)"),
            (w.EnemyMicros, "the enemy check"),
            (w.InspectMicros, "Spawn.Inspect of targets new to the mod"),
            (w.TargetMicros, "the target rules and game reads"),
        };
        var top = phases.MaxBy(x => x.Micros);
        var left = _preparing is not null ? "candidate discovery/order or enemy checks left for the next steps"
            : _scanOpen ? $"{_work.Count - _workAt} targets left for the next steps" : "the scan is done";
        _keyLine($"slow scan: {Ms(spent)} ms in one server step, most in {top.Name} ({Ms(top.Micros)} ms). " +
                 $"start {Ms(w.StartMicros)} ms: {w.Players} player(s), Players {Ms(w.PlayerMicros)} ms, {w.Finds} World.Find ({Ms(w.FindMicros)} ms, slowest call {Ms(w.SlowestFind)} ms), " +
                 $"{w.Candidates} candidates in the lists, {w.Reads} positions read ({Ms(w.ReadMicros)} ms, slowest call {Ms(w.SlowestRead)} ms), {w.InReach} in reach; " +
                 $"enemy check {Ms(w.EnemyMicros)} ms; Spawn.Inspect {Ms(w.InspectMicros)} ms: {w.New} new, slowest call {Ms(w.SlowestInspect)} ms" +
                 $"{(w.New > 0 ? $" (entity {w.SlowestEntity.Value})" : "")}; targets {Ms(w.TargetMicros)} ms: {w.Targets} looked at; " +
                 $"scan step {_scanSteps}, {left}");
    }

    /// <summary>Whether a "slow" key line may go out now (one per <see cref="SlowLineGapMs"/> for the scan and the
    /// smasher together); true takes the turn.</summary>
    internal bool SlowLineDue(long now)
    {
        if (_lastSlowLine is { } last && now - last < SlowLineGapMs) return false;
        _lastSlowLine = now;
        return true;
    }

    internal static string Ms(long micros) => (micros / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How far a player's scan looks: the largest target radius and the enemy check's distance.</summary>
    private static double Reach(PersonalSettings s) => Math.Max(LootRules.SearchRadius(s), s.EnemyPause ? s.EnemyDistance : 0);

    private bool Ready(PlayerLoot p, GamePlayer gp)
    {
        p.FirstSeenMs ??= _clock.NowMs;
        if (!Settled(p, _clock.NowMs))
        {
            Counters.SettingsWaits++;
            ProbeTrace.Note(gp.Entity, "player: waiting for the player's settings");
            return false;
        }
        var window = p.ReportsWindow ? p.WindowOpen : gp.UiOpen;
        if (window)
        {
            Counters.WindowPauses++;
            ProbeTrace.Note(gp.Entity, p.ReportsWindow ? "player: the F8 window or a game window is open (the player's game says so)"
                : "player: a game window is open (inventory, map, a container)");
            return false;
        }
        if (!p.Settings.Looting)
        {
            ProbeTrace.Note(gp.Entity, "player: auto loot is off (F9)");
            return false;
        }
        return true;
    }

    private IEnumerable<bool> ScanPlayer(PlayerLoot p, GamePlayer gp)
    {
        var s = p.Settings;
        Counters.PlayerScans++;
        if (!Ready(p, gp)) yield break;
        // Preparation also slices enemy classification, radius filtering and priority-queue insertion/removal.
        if (s.EnemyPause)
        {
            foreach (var turn in _index.Prepare(EnemyComponent)) yield return turn;
            foreach (var (e, at) in _index.Near(EnemyComponent))
            {
                var enemyStart = _micros();
                var inspectBefore = _step.InspectMicros;
                var pauses = at.DistanceTo(gp.Position) <= s.EnemyDistance &&
                    Classify(e) is { Rule: { } er } && Release.IsPausingEnemy(er, _game.IsDead(e));
                _step.EnemyMicros += _micros() - enemyStart - (_step.InspectMicros - inspectBefore);
                if (pauses)
                {
                    p.Enemy.EnemySeen();
                    Counters.EnemyNear++;
                }
                yield return true;
                if (pauses) break;
            }
        }
        if (p.Enemy.Paused(s.EnemyPause))
        {
            Counters.EnemyPauses++;
            ProbeTrace.Note(gp.Entity, "player: paused, an enemy is near (Pause near enemies)");
            yield break;
        }
        ProbeTrace.Note(gp.Entity, "player: looked around");
        Counters.PlayersLooting++;
        var ordered = new PriorityQueue<(EntityId Id, bool Spot), (int Kind, double Distance, int Order)>();
        var order = 0;
        if (s.Fish && _spotsDue)
        {
            foreach (var turn in _index.Prepare(FishingSpotComponent)) yield return turn;
            foreach (var (e, at) in _index.Near(FishingSpotComponent))
            {
                var d = at.DistanceTo(gp.Position);
                if (d <= s.Radius && !_spotsSaid.Contains(e)) ordered.Enqueue((e, true), (0, d, order++));
                yield return true;
            }
        }
        var radius = LootRules.SearchRadius(s);
        var seen = new HashSet<EntityId>();
        foreach (var component in new[] { PickupComponent, OfferComponent, InventoryComponent, LootMarkerComponent })
        {
            foreach (var turn in _index.Prepare(component)) yield return turn;
            foreach (var (e, at) in _index.Near(component))
            {
                var d = at.DistanceTo(gp.Position);
                if (d <= radius && !_playerIds.Contains(e) && seen.Add(e)) ordered.Enqueue((e, false), (1, d, order++));
                yield return true;
            }
        }
        while (ordered.TryDequeue(out var next, out _))
        {
            _work.Add((p, gp, next.Id, next.Spot));
            yield return true;
        }
    }

    private void Target(PlayerLoot p, GamePlayer gp, EntityId e)
    {
        if (_game.PositionOf(e) is not { } at) return;
        var dy = at.Y - gp.Position.Y;
        var d2 = DistanceSquared(gp.Position, at);
        var classified = Classify(e);
        // critters (fishing bait swarms), and fish while Fish is on: one kiln.log line each, taken or why not
        // (CritterWatch)
        if (classified is { Rule: { } wr, Key: var wk } && WatchOf(p, wr) is { } watch)
        {
            var critter = wr.Group == Group.Critter;
            if (watch.Seen(wk) && critter) Counters.CrittersSeen++;
            if (watch.Again(wk, _clock.NowMs, ms => WatchLine(p, wr, d2, dy,
                    $"still there {ms} ms after its take was sent: the game did not hand the {Thing(wr)} over " +
                    $"({LiveState(e)})")) is { } still)
            {
                if (critter) Counters.CrittersStuck++;
                _keyLine(still);
            }
        }
        if (!HeightAllows(p, gp, e, classified?.Rule, classified?.Key ?? 0, at)) return;
        if (classified is not { Rule: { } rule, Key: var key })
        {
            ProbeTrace.Note(e, "table: not in the template table (never taken)");
            Counters.UnknownTemplates++;   // a template not in templates.txt: never
            if (TemplateOf(e) is { } g && _critters.Unknown(g, () => _game.TemplateName(g), _data.Templates.BaitNote) is { } unknown)
                _keyLine(unknown);
            return;
        }
        if ((rule.Flags & TemplateFlags.Decor) != 0 && p.Settings.OldBooks && gp.NearBase)
        {
            // furniture the world placed that players also build (the books of villages and ruins): oldBooks off, the
            // group switch leaves it (LootRules); on, it still stays in and near a base, where it may be part of the
            // décor. Pressing E takes it either way
            ProbeTrace.Note(e, "decor: left, the player is in or near a base");
            Counters.DecorLeft++;
            return;
        }
        var hasPickup = _game.TryPickupItem(e, out var item);
        var liveItem = item;
        if (rule.Group == Group.Critter)
        {
            // a critter's pickup names no item (the bait is its default inventory): judged by the bait it gives
            if (item == 0) item = CritterItem(rule);
            if (item == 0)
            {
                ProbeTrace.Note(e, "critter: left, no known bait");
                WatchLeft(p, key, rule, d2, dy, "no known bait in the game data");
                return;
            }
        }
        if ((rule.Flags & TemplateFlags.Generic) != 0)
        {
            // a generic drop (broken barrels, pots, resource nodes): judged by the item the game gave it at the spawn;
            // without one it stays (a loot-all would take it unseen)
            Counters.GenericSeen++;
            if (!hasPickup || GenericDrops.Live(rule, item, _data.Items, _data.Quest) is not { } live)
            {
                ProbeTrace.Note(e, $"generic drop: left (pickup {hasPickup}, item {item})");
                Counters.GenericLeft++;
                return;
            }
            rule = live;
        }
        // a fish is grabbed as the pickup it is; without one the game shows nothing to grab (the container path holds
        // it)
        if (rule.Group == Group.Fish && !hasPickup)
            WatchLeft(p, key, rule, d2, dy, $"the game shows no pickup on it ({LiveState(e)})");
        var container = Release.IsContainerTarget(rule, hasPickup);
        ProbeTrace.Note(e, $"seen: {rule.Group} {rule.Flags}, {(container ? "container path" : $"pickup path, item {item}")}");
        if (container) Counters.ContainersSeen++;
        else Counters.PickupsSeen++;
        if (container) ContainerTarget(p, gp, e, key, rule, d2, at);
        else PickupTarget(p, gp, e, key, rule, d2, item, at, liveItem, dy);
    }

    // ---------------------------------------------------------------- critter and fish lines

    /// <summary>The kiln.log watch of a target: critters always; fish only while the player's Fish switch is on (it is
    /// off by default: no line for every fish a player swims past); null = no lines.</summary>
    private CritterWatch? WatchOf(PlayerLoot p, TemplateRule rule) => rule.Group switch
    {
        Group.Critter => _critters,
        Group.Fish when p.Settings.Fish => _fish,
        _ => null,
    };

    private static string Thing(TemplateRule rule) => rule.Group == Group.Fish ? "fish" : "bait";

    private string WatchLine(PlayerLoot p, TemplateRule rule, float d2, double dy, string outcome) =>
        $"{(rule.Group == Group.Fish ? "fish" : "critter")}: player {p.Entity} {rule.Name} {Math.Sqrt(d2):0.0} m away " +
        $"(dy {dy:+0.0;-0.0;0.0} m): {outcome}";

    private void WatchLeft(PlayerLoot p, ulong key, TemplateRule rule, float d2, double dy, string why)
    {
        if (WatchOf(p, rule)?.Left(key, WatchLine(p, rule, d2, dy, "left: " + why)) is { } line) _keyLine(line);
    }

    /// <summary>Fish on: one kiln.log line per fishing spot within the main radius (the radius fish use). Its fish are
    /// not in the water: the spot's effect draws them, the rod's minigame makes one from the spot's fish table when it
    /// bites, and its item exists only once caught, so there is nothing to take. Spots are looked for every
    /// <see cref="BlinkMs"/> and looked at in the scan's budgeted steps (<see cref="Classify"/>: Spawn.Inspect).</summary>
    private void FishingSpot(PlayerLoot p, GamePlayer gp, EntityId e)
    {
        if (_spotsSaid.Count > 4096) _spotsSaid.Clear();
        if (!_spotsSaid.Add(e) || _game.PositionOf(e) is not { } at) return;
        var key = (Classify(e)?.Key ?? KeyOf(e, 0)) | (1UL << 63);   // never a fish's key
        var name = TemplateOf(e) is { } g ? _game.TemplateName(g) : null;
        var line = _fish.Left(key,
            $"fish: player {p.Entity} is {Math.Sqrt(DistanceSquared(gp.Position, at)):0.0} m from a fishing spot" +
            $"{(name is null ? "" : $" ({name})")}: its fish come only with a fishing rod (the game makes the " +
            "fish when one bites), so Kiln Auto Loot cannot take them. It takes fish swimming free, the ones you " +
            "can grab by hand while swimming (mostly in the Veilwater Basin's lakes)");
        if (line is not null) _keyLine(line);
    }

    private string ItemText(uint item) =>
        _data.Items.TryGet(item, out var row) ? $"{item} {row.Internal}" : item.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What the game shows of a target now (critter lines).</summary>
    private string LiveState(EntityId e)
    {
        var pickup = _game.TryPickupItem(e, out var live) ? $"pickup item {live}" : "no pickup";
        var offer = _game.OfferOf(e);
        var o = offer.Present ? $"offer {offer.OfferId}{(offer.Disabled ? " disabled" : "")}" : "no offer";
        return $"{pickup}, own inventory {(_game.HasInventory(e) ? "yes" : "no")}, {o}";
    }

    private static string RefusalText(Verdict v, PersonalSettings s, TemplateRule rule) => v switch
    {
        Verdict.GroupOff => rule.Group == Group.Fish ? "Fish is off" : "Critters is off",
        Verdict.RadiusOut => $"beyond the main radius ({s.Radius:0} m)",
        Verdict.YieldIgnored => $"the {Thing(rule)} is on the skip list",
        Verdict.NotListed => $"the {Thing(rule)} is not on the only list",
        Verdict.NotFocus => "the gather focus is on another item",
        _ => v.ToString(),
    };

    // ---------------------------------------------------------------- container path

    private void ContainerTarget(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, float d2, WorldPosition at)
    {
        var s = p.Settings;
        var dead = rule.Group == Group.Enemy && _game.IsDead(e);
        if (rule.Group == Group.Enemy && !dead) return;   // a living enemy: never looted (gate step 3)
        if (!Release.ContainerReleased(rule, dead))
        {
            ProbeTrace.Note(e, "container: not released");
            Counters.ContainersHeldBack++;   // gate step 4
            UnreadLine(p, e, key, rule, d2, () => "it is boss loot, which Auto Loot always leaves for you");
            return;
        }
        var verdict = LootRules.Decide(rule, released: true, d2, s, p.Filter, out var noticeItem, gp.NearBase);
        if (verdict != Verdict.Allowed)
        {
            ProbeTrace.Note(e, $"container: refused ({verdict})");
            CountRefusal(p, verdict, noticeItem);
            UnreadLine(p, e, key, rule, d2, () => CorpseRefusal(verdict, rule, s));
            return;
        }
        if (Release.IsHarvest(rule) && !HarvestFits(p, gp, e, rule, out _))
        {
            ProbeTrace.Note(e, "container: the harvest does not fit the bag rules");
            return;   // checked before the accept too
        }
        if (Release.IsLivestock(rule))
        {
            // farm animals live in a base: a player whose user group may not access inventories (the game gives it no
            // prompt at a chest) gets none of their produce; the slot moves would get past the game's own checks
            if (!gp.MayOpenInventories)
            {
                ProbeTrace.Note(e, "livestock: left, the player's user group may not access inventories");
                Counters.LivestockNoAccess++;
                return;
            }
            // read less than LivestockEveryMs ago: not due yet, so no sight check and no try (a base keeps hundreds)
            if (p.LivestockNext.TryGetValue(key, out var next) && _clock.NowMs < next)
            {
                ProbeTrace.Note(e, "livestock: read again later");
                return;
            }
        }
        var bodyCorpse = dead || (rule.Group == Group.Corpse && !Release.IsDrop(rule));
        if (!InSight(p, gp, e, key, rule, at, bodyCorpse))
        {
            ProbeTrace.Note(e, "container: not in sight");
            if (HiddenNow(p, key))
                UnreadLine(p, e, key, rule, d2, () => "it is not in plain view from where you stand (Only what I can see)");
            return;
        }
        if (_budget <= 0)
        {
            ProbeTrace.Note(e, "container: the scan's send budget is spent");
            Counters.CapReached++;   // the visit table is not asked: its answer would be spent without a send
            return;
        }
        Counters.ContainerTries++;
        var offer = _game.OfferOf(e);
        if (Release.IsLivestock(rule))
        {
            // a farm animal: its produce is read from its inventory and taken (loot-all, or slot moves when something
            // stays), never with an accept; it refills slowly, so it is read again every LivestockEveryMs, not "done"
            var now = _clock.NowMs;
            if (p.LivestockNext.Count >= LivestockMemory)
                foreach (var old in p.LivestockNext.Where(x => x.Value <= now).Select(x => x.Key).ToList()) p.LivestockNext.Remove(old);
            p.LivestockNext[key] = now + LivestockEveryMs;
            Counters.LivestockReads++;
            ProbeTrace.Note(e, "livestock: produce read");
            TakeFromContainer(p, gp, e, key, rule, offer);
            return;
        }
        var step = p.Visits.Next(key, offer.Usable, offer.OfferId, s.OpenDelay, Release.HarvestOnce(rule));
        ProbeTrace.Note(e, $"container: step {step} (offer present {offer.Present}, disabled {offer.Disabled}, id {offer.OfferId})");
        switch (step)
        {
            case TargetStep.Wait:
                Counters.Waiting++;
                break;
            case TargetStep.AlreadyHarvested:
                Counters.AlreadyHarvested++;
                break;
            case TargetStep.Accept:
                // step 5: the accept alone; the loot-all follows in a later scan
                _budget--;
                Counters.Injects++;
                Counters.Accepts++;
                _game.Interact(gp.Entity, e, offer.OfferId);
                ProbeTrace.Note(e, $"send: accept (offer {offer.OfferId})");
                p.Visits.Accepted(key, offer.OfferId);
                break;
            case TargetStep.Loot:
                TakeFromContainer(p, gp, e, key, rule, offer);
                break;
        }
    }

    /// <summary>Step 6: loot-all, or Items.Move per slot when the slot filter keeps something.</summary>
    private void TakeFromContainer(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, Offer offer)
    {
        if (p.PendingMoves.ContainsKey(key))
        {
            var age = _clock.NowMs - p.PendingSince.GetValueOrDefault(key, _clock.NowMs);
            ProbeTrace.Note(e, $"loot: Items.Move still pending ({age} ms); no duplicate queued");
            if (age >= MovePendingMs)
                MoveLine(p, key, 0, "pending", () => $"move: player {p.Entity} source {e} {rule.Name}: pending {age} ms; " +
                    $"{p.PendingMoves[key]} result(s) not received; no duplicate queued");
            return;
        }
        if (p.MoveRetryAt.TryGetValue(key, out var retry))
        {
            if (_clock.NowMs < retry)
            {
                ProbeTrace.Note(e, "loot: unsuccessful Items.Move waits for its retry delay");
                return;
            }
            p.MoveRetryAt.Remove(key);
        }
        var s = p.Settings;
        if (Release.IsHarvest(rule))
        {
            // plants and crops: the loot-all is a harvest, taken whole; the item filter judged its yield before, the bag
            // judges its live contents now
            if (!HarvestFits(p, gp, e, rule, out var contents)) return;
            if (SendLoot(p, gp, e, key, offer) && contents is not null) Reserve(p, contents);
            return;
        }
        // a loot-all takes everything, so it goes out only after a read showed nothing has to stay; an inventory that
        // cannot be read (kiln.items off, a failed read) is left and tried again in the next scan
        var slots = _game.Slots(e);
        if (slots is null && Release.LootUnread(rule))
        {
            // a fog-nest crystal or a creature's pile (bones, bat fur): its contents come with the loot-all only; the
            // item filter judged a crystal's yield before, a pile's is judged here, and a bag rule that cannot see the
            // amounts leaves it (as for a harvest)
            ProbeTrace.Note(e, "loot: unread ground loot or drop, loot-all like a harvest");
            TakeUnread(p, gp, e, key, rule, offer, readEmpty: false);
            return;
        }
        if (slots is null)
        {
            ProbeTrace.Note(e, "loot: the inventory cannot be read");
            Counters.Unreadable++;
            return;
        }
        ProbeTrace.Note(e, $"loot: slots [{string.Join(", ", slots.Select(x => $"{x.Item} x{x.Count}"))}]");

        // what each slot gives: the whole slot (count 0), part of it (the bag's allowance), or nothing
        var take = new List<(ContainerSlot Slot, int Count)>();
        var kept = false;
        var takeable = 0;
        // planned on a copy of the bag: each slot's amount is reserved at once, so later slots see shared group limits
        // and the free slots earlier ones use; the real bag counts only what is actually sent
        var plan = BagRules.NeedsBag(s, p.Limits) ? BagOf(p, gp)?.Clone() : null;
        // what stays and why, for the target's kiln.log line (collected only until that line is written)
        var left = _left.Told(key) ? null : new List<(uint Item, int Count, string Why)>();
        foreach (var slot in slots)
            switch (LootRules.Slot(slot.Item, slot.Rarity, s, _data.Quest, p.Filter, _data.Items))   // quest, filter, rarity, take
            {
                case SlotRule.KeepQuest:
                    Counters.QuestKept++;
                    kept = true;
                    left?.Add((slot.Item, slot.Count, "a quest item"));
                    break;
                case SlotRule.KeepIgnored:
                    Counters.SkippedInContainers++;
                    kept = true;
                    left?.Add((slot.Item, slot.Count, "on the skip list"));
                    LeftBehind(p, slot.Item);
                    break;
                case SlotRule.KeepNotListed:
                    Counters.NotListed++;
                    kept = true;
                    left?.Add((slot.Item, slot.Count, "not on the only list"));
                    break;
                case SlotRule.KeepNotFocus:
                    Counters.NotFocus++;
                    kept = true;
                    left?.Add((slot.Item, slot.Count, "not the gather focus"));
                    break;
                case SlotRule.KeepRarity:
                    Counters.RarityLow++;
                    kept = true;
                    left?.Add((slot.Item, slot.Count, "gear below \"Gear from this rarity on\""));
                    break;
                default:
                    var allowed = BagRules.Allowance(slot.Item, s, plan, p.Limits, out var why);
                    if (allowed <= 0)
                    {
                        var refusal = why == BagRefusal.None ? BagRefusal.NoRoom : why;
                        CountBag(refusal);
                        kept = true;
                        left?.Add((slot.Item, slot.Count, BagText(refusal)));
                        break;
                    }
                    var count = allowed >= slot.Count ? 0 : allowed;   // 0 = the whole slot
                    if (count > 0)
                    {
                        kept = true;                                    // part of the slot stays
                        left?.Add((slot.Item, slot.Count - count, "the rest of the stack, past a bag rule"));
                    }
                    var moving = count == 0 ? slot.Count : count;
                    plan?.Took(slot.Item, moving);
                    take.Add((slot, count));
                    takeable += moving;
                    break;
            }

        ProbeTrace.Note(e, $"loot: {take.Count} slots to take, something kept: {kept}");
        if (left is { Count: > 0 }) LeftLine(p, key, rule, left, take);
        if (take.Count == 0)
        {
            // nothing to take: corpses and chests stay as they are; others still get the loot-all (harvest-like), a
            // creature's pile as when it cannot be read (its default inventory comes with the loot-all)
            if (kept || Release.SkipWhenEmpty(rule)) return;
            if (Release.IsDrop(rule)) TakeUnread(p, gp, e, key, rule, offer, readEmpty: true);
            else SendLoot(p, gp, e, key, offer);
            return;
        }

        // containers: every item let through counts for the 100-in-5-s rule.
        foreach (var (slot, _) in take)
            if (p.Full.ContainerAttempt(slot.Item)) InventoryFull(p);
        // Also: a container whose takeable contents did not change since the last try while the player has no free slot
        // is "full" now (a 10 Hz scan never reaches 100 repeats), and is retried once per second until there is room.
        var now = _clock.NowMs;
        var free = _game.FreeSlots(gp.Entity);
        if (free == 0 && p.Stalls.TryGetValue(key, out var stall) && stall.Takeable == takeable)
        {
            if (p.Full.MayNotify()) InventoryFull(p);
            if (now - stall.At < StallRetryMs) return;
        }
        if (free == 0) p.Stalls[key] = (takeable, now);
        else p.Stalls.Remove(key);

        if (!kept)
        {
            if (!SendLoot(p, gp, e, key, offer)) return;
            foreach (var (slot, _) in take)
            {
                Counters.CountLooted(slot.Item, slot.Count);
                p.Bag?.Took(slot.Item, slot.Count);
            }
            return;
        }

        var sent = 0;
        foreach (var (slot, count) in take)
        {
            if (_budget <= 0)
            {
                Counters.CapReached++;
                break;
            }
            _budget--;
            sent++;
            Counters.Moves++;
            var moving = count == 0 ? slot.Count : count;
            p.Bag?.Took(slot.Item, moving);
            p.PendingMoves[key] = p.PendingMoves.GetValueOrDefault(key) + 1;
            p.PendingSince.TryAdd(key, _clock.NowMs);
            var item = slot.Item;
            p.MovesInFlight[item] = p.MovesInFlight.GetValueOrDefault(item) + moving;
            ProbeTrace.Note(e, $"send: move slot {slot.Id} ({item} x{moving})");
            var player = gp.Entity;
            var replied = false;
            _game.Move(e, slot.Id, gp.Entity, count, answer =>
                {
                    if (replied) return;  // a duplicate callback cannot release another queued move's reservation
                    replied = true;
                    MoveAnswered(p, key, e, rule, slot.Id, answer, item, moving);
                },
                entities =>
                {
                    if (entities.Count > 0) Moved?.Invoke(player, e, entities);
                });
        }
        if (sent > 0)
        {
            CountLootKind(offer);
            // The result, not dispatch, counts a successful slot take. Ground/drop visits still need their loot-all.
        }
    }

    private void MoveAnswered(PlayerLoot p, ulong key, EntityId source, TemplateRule rule, ulong slot, MoveAnswer answer, uint item, int count)
    {
        // A world stopped or the player left while the service was answering: no state/notice in the new session.
        if (!_players.TryGetValue(p.Entity, out var current) || !ReferenceEquals(current, p)) return;
        if (p.MovesInFlight.TryGetValue(item, out var flying))
        {
            if (flying <= count) p.MovesInFlight.Remove(item);
            else p.MovesInFlight[item] = flying - count;
        }
        if (p.PendingMoves.TryGetValue(key, out var n))
        {
            if (n <= 1)
            {
                p.PendingMoves.Remove(key);
                p.PendingSince.Remove(key);
            }
            else p.PendingMoves[key] = n - 1;
        }
        p.BagScan = -1;  // discard the speculative reservation; the next decision reads the actual bag
        if (answer.Confirmed > 0) Counters.CountLooted(item, Math.Min(count, answer.Confirmed));
        var unsuccessful = answer.Outcome != MoveOutcome.Done || (answer.Confirmed >= 0 && answer.Confirmed < count);
        if (unsuccessful)
        {
            if (p.MoveRetryAt.Count >= 1024) p.MoveRetryAt.Clear();
            p.MoveRetryAt[key] = _clock.NowMs + MoveRetryMs;
        }
        else if (rule.Group != Group.Ground && !Release.IsDrop(rule)) p.Visits.Looted(key);
        var status = string.IsNullOrEmpty(answer.Status) ? answer.Outcome.ToString() : answer.Status;
        var confirmed = answer.Confirmed < 0 ? "unknown" : answer.Confirmed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MoveLine(p, key, slot, status + "/" + confirmed, () => $"move: player {p.Entity} source {source} {rule.Name} slot {slot}, " +
            $"{ItemName(item)} requested {count}: {CleanMoveText(status)}; confirmed by Items.Move ledger {confirmed}, service reported {answer.Reported}; " +
            $"{CleanMoveText(answer.Detail)}; {(unsuccessful ? $"retry after {MoveRetryMs} ms with a fresh source read" : "answered")}");
        ProbeTrace.Note(source, $"move result: {status}, ledger confirms {confirmed}/{count}");
        if (answer.Outcome == MoveOutcome.NoRoom && p.Full.MayNotify()) InventoryFull(p);
    }

    private void MoveLine(PlayerLoot p, ulong key, ulong slot, string state, Func<string> text)
    {
        if (_moveLines >= MaxMoveLines)
        {
            if (!_moveCapSaid)
            {
                _moveCapSaid = true;
                _keyLine($"move: {MaxMoveLines} Items.Move result/pending lines in this world; the rest are left out");
            }
            return;
        }
        if (!_moveNotes.Add((p.Entity, key, slot, state))) return;
        _moveLines++;
        _keyLine(text());
    }

    private static string CleanMoveText(string text) => text.Replace('\r', ' ').Replace('\n', ' ') is { } clean
        ? clean[..Math.Min(clean.Length, 240)] : "";

    /// <summary>The loot-all of a target whose contents cannot be seen (a fog-nest crystal, a creature's pile): taken whole.
    /// The crystal's yield was judged by LootRules.Decide. A pile's is judged here, as it comes whole: in skip mode one
    /// whose yield holds a skipped item stays; a picking filter (only list, gather focus) needs a yield item it takes
    /// (Decide lets a corpse without a known yield through for its slots, which cannot be seen here). With a bag rule on
    /// the contents have to fit (HarvestFits; with keepFreeSlots alone, the most its yield can give). A pile left whole
    /// says so once in kiln.log (PileLine).
    /// <paramref name="readEmpty"/>: its inventory was read and empty (else it cannot be read).</summary>
    private void TakeUnread(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, Offer offer, bool readEmpty)
    {
        if (Release.IsDrop(rule))
        {
            if (p.Filter.Picks)
            {
                if (!rule.Yield.Any(p.Filter.Allows))
                {
                    ProbeTrace.Note(e, "loot: the pile gives nothing the filter takes");
                    var focus = p.Filter.Focus != 0;
                    if (focus) Counters.NotFocus++;
                    else Counters.NotListed++;
                    PileLine(p, key, rule, readEmpty, rule.Yield.Count == 0
                        ? $"what it gives is not known, and {(focus ? "the gather focus" : "the only list")} takes a pile only for an item it can give"
                        : focus ? "nothing it can give is the gather focus" : "nothing it can give is on the only list");
                    return;
                }
            }
            else if (LootRules.FirstIgnoredYield(rule, p.Filter.Skip) is var skipped and not 0)
            {
                ProbeTrace.Note(e, $"loot: the pile's yield holds skipped item {skipped}");
                Counters.YieldSkipped++;
                LeftBehind(p, skipped);
                PileLine(p, key, rule, readEmpty, $"it can give {ItemName(skipped)} (on the skip list)");
                return;
            }
        }
        if (HarvestFits(p, gp, e, rule, out var contents))
        {
            if (SendLoot(p, gp, e, key, offer) && contents is not null) Reserve(p, contents);
        }
        else PileLine(p, key, rule, readEmpty, BagRules.SlotsOnly(rule.Yield, p.Settings, p.Limits)
            ? "the most it can give fits neither the stacks you carry nor the bag slots free above the ones kept free"
            : "a bag rule is on, and it needs the amounts");
    }

    /// <summary>Reserves a whole take that went (HarvestFits' contents) on the scan's bag; item 0 = one new stack of
    /// something not known.</summary>
    private static void Reserve(PlayerLoot p, List<(uint Item, int Count)> contents)
    {
        foreach (var (item, count) in contents)
            if (item == 0) p.Bag?.TookNewStacks(count);
            else p.Bag?.Took(item, count);
    }

    /// <summary>The kiln.log line of a target that keeps something (once per target): what stays and why, and what is
    /// taken. A corpse or creature pile stays in the world while it holds anything.</summary>
    private void LeftLine(PlayerLoot p, ulong key, TemplateRule rule, List<(uint Item, int Count, string Why)> left,
        List<(ContainerSlot Slot, int Count)> take)
    {
        var line = _left.Left(key, () =>
        {
            var stays = string.Join(", ", left.Select(x => $"{ItemName(x.Item)} x{x.Count} ({x.Why})"));
            var taking = take.Count == 0
                ? "nothing else to take"
                : "taking " + string.Join(", ", take.Select(x => $"{ItemName(x.Slot.Item)} x{(x.Count == 0 ? x.Slot.Count : x.Count)}"));
            var stay = rule.Group is Group.Corpse or Group.Enemy
                ? $"; the game removes a {(Release.IsDrop(rule) ? "creature pile" : "corpse")} only once it is empty, so this one stays"
                : "";
            return $"loot: player {p.Entity} {rule.Name}: left {stays}; {taking} (planned, not receipt; Items.Move result follows){stay}";
        });
        if (line is not null) _keyLine(line);
    }

    /// <summary>The kiln.log line of a creature pile left whole (once per pile): its contents cannot be seen, only its
    /// loot-all takes them, all at once, and <paramref name="why"/> that cannot go out.</summary>
    private void PileLine(PlayerLoot p, ulong key, TemplateRule rule, bool readEmpty, string why)
    {
        var line = _left.Left(key, () =>
            $"loot: player {p.Entity} {rule.Name}: left whole: its inventory {(readEmpty ? "reads empty" : "cannot be read")}, " +
            $"so only its loot-all can take it, all at once, and {why}");
        if (line is not null) _keyLine(line);
    }

    /// <summary>The kiln.log line of a corpse (or boss loot) in reach that the loop does not look into, once per corpse:
    /// a player's "it never touched that corpse" says where to look (the height window, a radius, sight, a switch).</summary>
    private void UnreadLine(PlayerLoot p, EntityId e, ulong key, TemplateRule rule, float d2, Func<string> why)
    {
        if (_unread.Told(key)) return;
        if (!(rule.Group is Group.Corpse or Group.Boss || (rule.Group == Group.Enemy && _game.IsDead(e)))) return;
        var line = _unread.Left(key, () =>
            $"loot: player {p.Entity} {rule.Name} {Math.Sqrt(d2):0.0} m away: not looked into while {why()}");
        if (line is not null) _keyLine(line);
    }

    private static string CorpseRefusal(Verdict v, TemplateRule rule, PersonalSettings s) => v switch
    {
        Verdict.GroupOff when (rule.Flags & (TemplateFlags.Boss | TemplateFlags.Elite)) != 0 && s.Corpses =>
            "it is a boss, mini boss or elite corpse, and Bosses is off",
        Verdict.GroupOff => "Corpses is off",
        Verdict.RadiusOut => $"it is beyond the corpse radius ({LootRules.GroupRadius(Group.Corpse, s):0} m)",
        Verdict.YieldIgnored => "everything it can give is on the skip list",
        Verdict.NotListed => "nothing it can give is on the only list",
        Verdict.NotFocus => "nothing it can give is the gather focus",
        _ => v.ToString(),
    };

    private static string BagText(BagRefusal why) => why == BagRefusal.CarryLimit ? "your carry limit" : "no room under your bag rules";

    /// <summary>An item's name for kiln.log: English, else the internal name, else the id.</summary>
    private string ItemName(uint item) =>
        _data.Items.TryGet(item, out var row) && (row.English.Length > 0 ? row.English : row.Internal) is { Length: > 0 } name
            ? name
            : item.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Returns whether the loot-all went out (false: the per-scan cap is reached).</summary>
    private bool SendLoot(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, Offer offer)
    {
        if (_budget <= 0)
        {
            Counters.CapReached++;
            return false;
        }
        _budget--;
        Counters.Injects++;
        CountLootKind(offer);
        ProbeTrace.Note(e, $"send: loot-all (offer present {offer.Present}, disabled {offer.Disabled})");
        _game.Loot(gp.Entity, e);
        p.Visits.Looted(key);
        return true;
    }

    private void CountLootKind(Offer offer)
    {
        if (!offer.Present) Counters.LootNoOffer++;
        else if (offer.Disabled) Counters.LootOfferOff++;
        else Counters.LootAlls++;
    }

    // ---------------------------------------------------------------- pickup path

    private void PickupTarget(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, float d2, uint item, WorldPosition at,
        uint liveItem = 0, double dy = 0)
    {
        var s = p.Settings;
        var watch = WatchOf(p, rule);   // critters, and fish while Fish is on: one kiln.log line each
        string? why = null;   // a watched target's lasting reason to stay (its kiln.log line)
        var verdict = LootRules.Decide(rule, released: false, d2, s, p.Filter, out var noticeItem, gp.NearBase);
        var wanted = verdict == Verdict.Allowed;
        if (!wanted)
        {
            ProbeTrace.Note(e, $"pickup: refused ({verdict})");
            CountRefusal(p, verdict, noticeItem);
            why = RefusalText(verdict, s, rule);
        }
        var details = _game.PickupDetails(e) ?? new GroundItem(1, GearRules.UnknownRarity);
        if (wanted)   // the pickup's own item: the item filter, then the rarity rule
            switch (p.Filter.Check(item))
            {
                case ItemRefusal.Skipped:
                    wanted = false;
                    Counters.SkippedPickups++;
                    LeftBehind(p, item);
                    why = RefusalText(Verdict.YieldIgnored, s, rule);
                    break;
                case ItemRefusal.NotListed:
                    wanted = false;
                    Counters.NotListed++;
                    why = RefusalText(Verdict.NotListed, s, rule);
                    break;
                case ItemRefusal.NotFocus:
                    wanted = false;
                    Counters.NotFocus++;
                    why = RefusalText(Verdict.NotFocus, s, rule);
                    break;
                default:
                    if (GearRules.BelowMin(item, details.Rarity, s.MinGearRarity, _data.Items))
                    {
                        wanted = false;
                        Counters.RarityLow++;
                    }
                    break;
            }
        // the bag: a pickup is taken whole, so its whole amount has to fit
        if (wanted && BagRules.NeedsBag(s, p.Limits) &&
            CountBag(BagRules.Pickup(item, details.Amount, s, BagOf(p, gp), p.Limits)))
        {
            wanted = false;
            why = "a bag rule (no room for it, or a carry limit)";
        }
        var withInventory = _game.HasInventory(e);
        ProbeTrace.Note(e, $"pickup: wanted after the item, rarity and bag rules: {wanted} (amount {details.Amount}, " +
                           $"rarity {details.Rarity}, own inventory {withInventory})");
        // takeInPlace off: static pickups (with an inventory) cannot be pulled: only within 0.75 m.
        if (wanted && withInventory && !s.TakeInPlace && d2 > StaticPickupNearSquared)
        {
            wanted = false;
            why = "Take where it lies is off and it is more than 0.75 m away";
        }
        if (wanted && !InSight(p, gp, e, key, rule, at))
        {
            wanted = false;
            // a check the step's shared ray budget could not answer yet is no reason to leave it (no line)
            if (HiddenNow(p, key)) why = "not in plain view (Only what I can see)";
        }
        if (watch is not null && why is not null) WatchLeft(p, key, rule, d2, dy, why);
        if (_budget <= 0)
        {
            if (wanted) Counters.CapReached++;
            return;   // the tracker is not asked: no episode time passes without a try
        }
        var may = p.Pickups.MayTake(key, wanted, out var raiseFull);
        if (raiseFull) InventoryFull(p);
        if (!may)
        {
            ProbeTrace.Note(e, $"pickup: not now (wanted {wanted})");
            if (wanted) Counters.PickupWaits++;
            return;
        }

        if (withInventory)
        {
            // step 6: mushrooms, stone piles: the first sighting of a visit sends one accept, then the loot-all in the
            // same scan (no openDelay on this path).
            Counters.FixedPickups++;
            var offer = _game.OfferOf(e);
            if (Release.PickupReleased(rule) && offer.Usable && _budget >= 2 && p.Visits.FirstAcceptDue(key))
            {
                _budget--;
                Counters.Injects++;
                Counters.Accepts++;
                _game.Interact(gp.Entity, e, offer.OfferId);
                ProbeTrace.Note(e, $"send: accept before the pickup (offer {offer.OfferId})");
                p.Visits.Accepted(key, offer.OfferId);
            }
        }
        _budget--;
        Counters.Injects++;
        Counters.CountLooted(item, details.Amount);
        p.Bag?.Took(item, details.Amount);
        ProbeTrace.Note(e, $"send: pickup loot ({item} x{details.Amount})");
        _game.Loot(gp.Entity, e);
        p.Pickups.Taken(key);
        if (watch is not null)
        {
            var line = watch.Sent(key, _clock.NowMs, WatchLine(p, rule, d2, dy,
                $"loot sent ({Thing(rule)} {ItemText(item)}; the game shows {LiveState(e)}, live pickup item {liveItem})"),
                out var first);
            if (first && rule.Group == Group.Critter) Counters.CrittersSent++;
            if (line is not null) _keyLine(line);
        }
    }

    // ---------------------------------------------------------------- helpers

    private void CountRefusal(PlayerLoot p, Verdict verdict, uint noticeItem)
    {
        switch (verdict)
        {
            case Verdict.GroupOff: Counters.GroupOff++; break;
            case Verdict.RadiusOut: Counters.OutOfRange++; break;
            case Verdict.UnknownTemplate: Counters.UnknownTemplates++; break;
            case Verdict.NotReleased: Counters.NotReleased++; break;
            case Verdict.YieldIgnored:
                Counters.YieldSkipped++;
                if (noticeItem != 0) LeftBehind(p, noticeItem);
                break;
            case Verdict.NotListed: Counters.NotListed++; break;
            case Verdict.NotFocus: Counters.NotFocus++; break;
        }
    }

    /// <summary>The player's bag for this scan: read once at the scan's first need (Items.Read of the player), then
    /// kept up to date with what the scan takes. Null when it cannot be read.</summary>
    private Bag? BagOf(PlayerLoot p, GamePlayer gp)
    {
        if (p.BagScan == _scanNumber) return p.Bag;
        p.BagScan = _scanNumber;
        var read = _game.Bag(gp.Entity);
        p.Bag = read is null ? null : new Bag(read.Slots, read.FreeSlots, MaxStackOf);
        // moves sent in an earlier scan that the game has not answered yet are not in the read
        if (p.Bag is not null) foreach (var (item, n) in p.MovesInFlight) p.Bag.Took(item, n);
        return p.Bag;
    }

    /// <summary>The max stack the world uses now for an item: the game's own (the item table's original) raised the
    /// way the server's maxStack setting raised it in this world. 0 = not known (an item missing from the table).</summary>
    private int MaxStackOf(uint item) =>
        _data.Items.TryGet(item, out var row) && row.OriginalMaxStack > 0
            ? WorldDataRules.StackTarget(row.OriginalMaxStack, _game.StackSizeWish)
            : 0;

    /// <summary>A harvest (plant, crop) is taken whole. While a bag rule applies to it (fillCarriedStacks,
    /// keepFreeSlots, or a carry limit naming one of its items or their group) its own live inventory is read for the
    /// exact items and amounts, and all of them have to fit. One that cannot be read or reads empty (most plants, every
    /// crop, most creature piles: no inventory of their own, the loot-all hands their DefaultInventory over) is judged
    /// by the most its yield can give (countMax times the world's Loot Amount, BagRules.FitsUnknownAmounts) while
    /// keepFreeSlots is the only rule that applies, else left (counted: fillCarriedStacks and carry limits need the
    /// amounts). Without a bag rule nothing is read. <paramref name="contents"/>: what to reserve when it goes.</summary>
    private bool HarvestFits(PlayerLoot p, GamePlayer gp, EntityId e, TemplateRule rule, out List<(uint Item, int Count)>? contents)
    {
        contents = null;
        var s = p.Settings;
        IReadOnlyList<ContainerSlot>? slots = null;
        var applies = BagRules.Applies(rule.Yield, s, p.Limits);
        if (!applies && !p.Limits.IsEmpty && (slots = _game.Slots(e)) is not null)
            applies = BagRules.Applies(slots.Select(x => x.Item), s, p.Limits);
        if (!applies) return true;
        slots ??= _game.Slots(e);
        if (slots is null || slots.Count == 0)
        {
            // most plants, every crop and most creature piles have no inventory of their own (a DefaultInventory the
            // loot-all hands over): keepFreeSlots alone is kept by the most they can give (0.2.13)
            if (BagRules.SlotsOnly(rule.Yield, s, p.Limits))
            {
                var refused = CountBag(BagRules.FitsUnknownAmounts(rule.Yield, rule.YieldMax, _game.LootAmountFactor, s,
                    BagOf(p, gp), p.Limits, out var reserve));
                if (!refused) contents = reserve;
                return !refused;
            }
            Counters.HarvestUnread++;   // the amounts are not known: fillCarriedStacks or a carry limit cannot be kept
            return false;
        }
        contents = slots.Select(x => (x.Item, x.Count)).ToList();
        return !CountBag(BagRules.Fits(contents, s, BagOf(p, gp), p.Limits));
    }

    /// <summary>Counts a bag refusal; true = refused.</summary>
    private bool CountBag(BagRefusal why)
    {
        switch (why)
        {
            case BagRefusal.NoRoom: Counters.NoRoom++; return true;
            case BagRefusal.CarryLimit: Counters.CarryLimited++; return true;
            default: return false;
        }
    }

    /// <summary>The height fallback is checked both before sight and on the first Unknown answer itself.</summary>
    private bool HeightAllows(PlayerLoot p, GamePlayer gp, EntityId e, TemplateRule? rule, ulong key, WorldPosition at)
    {
        var dy = at.Y - gp.Position.Y;
        if ((rule is not null && !HeightWindow.AppliesTo(rule.Group)) || SightDecides(p) || HeightWindow.Allows(dy, p.Settings)) return true;
        ProbeTrace.Note(e, $"height: outside the height window fallback (dy {dy:0.0} m)");
        Counters.HeightOut++;
        var outside = dy < 0
            ? $"{-dy:0.0} m below you, deeper than Depth below me ({p.Settings.HeightBelow:0} m)"
            : $"{dy:0.0} m above you, higher than Height above me ({p.Settings.HeightAbove:0} m)";
        var d2 = DistanceSquared(gp.Position, at);
        if (rule is { Group: Group.Fish }) WatchLeft(p, key, rule, d2, dy, outside);
        if (rule is not null) UnreadLine(p, e, key, rule, d2, () => "it is " + outside);
        return false;
    }

    /// <summary>Line of sight decides what is in reach for this player (0.2.8): "Only what I can see" is on, the server
    /// can check it, and it answered lately. Otherwise the height window stands in (Target).</summary>
    private bool SightDecides(PlayerLoot p) =>
        p.Settings.VisibleOnly && _game.SightAvailable && _clock.NowMs >= _sightUnknownUntil;

    /// <summary>Line of sight: only with visibleOnly; cached about 1 s per target; "not known" = visible, and the height
    /// window stands in for <see cref="SightRetryMs"/> (or until a check answers again).</summary>
    private bool InSight(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, WorldPosition targetAt, bool bodyCorpse = false)
    {
        if (!p.Settings.VisibleOnly)
        {
            p.PendingSight.Remove(key);
            return true;
        }
        var now = _clock.NowMs;
        if (p.Sight.TryGetValue(key, out var c) && c.Until > now)
        {
            c.Trace.Note(e, cached: true);
            return c.Visible;
        }
        SightTrace trace;
        var nextSample = SightSample.CorpseBody;
        var rays = 1; // the completed primary voxel hit; Unknown body answers also consume an attempt
        var pendingUntil = now + SightCacheMs;
        if (p.PendingSight.TryGetValue(key, out var pending) && bodyCorpse && pending.Until > now &&
            pending.Trace.From == gp.Position && pending.Trace.To == targetAt)
        {
            trace = pending.Trace; // same entity/geometry: never repeat a completed ray while continuing
            nextSample = pending.Next;
            rays = pending.Rays;
            pendingUntil = pending.Until;
            trace.Note(e, cached: true);
            return BodySamples();
        }
        p.PendingSight.Remove(key);
        var primary = _game.LineOfSight(gp.Entity, e, corpse: bodyCorpse);
        trace = new SightTrace(gp.Position, targetAt, primary);
        trace.Note(e);
        switch (primary.State)
        {
            case Sight.Later:
                _retrySightNextStep = bodyCorpse;
                return false;
            case Sight.Hidden when bodyCorpse && primary.Obstruction is not null:
                _sightUnknownUntil = 0;
                return BodySamples();
            case Sight.Hidden:
                _sightUnknownUntil = 0;
                Counters.SightBlocked++;
                Remember(false);
                return false;
            case Sight.Unknown:
                _sightUnknownUntil = now + SightRetryMs;
                Remember(true);
                return HeightAllows(p, gp, e, rule, key, targetAt);
            default:
                _sightUnknownUntil = 0;
                Remember(true);
                return true;
        }

        bool BodySamples()
        {
            while (true)
            {
                // Each completed hit advances the stage before the next budget check. A continuation therefore spends
                // no more primary/low rays on a corpse whose remaining sample still needs the shared ray budget.
                if (rays >= 3)
                {
                    SavePending();
                    ProbeTrace.Note(e, "sight: three ray attempts spent; incomplete body check held until reevaluation, no take and no hidden cache");
                    return false;
                }
                if (_micros() >= _stepEnd)
                {
                    SavePending();
                    _retrySightNextStep = true;
                    ProbeTrace.Note(e, $"sight: {nextSample} waits for a later step's time budget; completed voxel hits retained");
                    return false;
                }
                var body = _game.LineOfSight(gp.Entity, e, nextSample, corpse: true);
                if (body.State != Sight.Later) rays++; // BudgetExceeded did not cast a ray; Unknown is counted conservatively
                trace = nextSample == SightSample.CorpseBody ? trace with { Body = body } : trace with { Upper = body };
                trace.Sample(e, nextSample, body);
                switch (body.State)
                {
                    case Sight.Visible:
                        _sightUnknownUntil = 0;
                        Remember(true);  // only a definitively clear ray can rescue the known voxel hits
                        return true;
                    case Sight.Hidden when nextSample == SightSample.CorpseBody && body.Obstruction is not null:
                        nextSample = SightSample.CorpseUpper; // one more small height in the same body-origin column
                        continue;
                    case Sight.Hidden:
                        _sightUnknownUntil = 0;
                        Counters.SightBlocked++;
                        Remember(false);
                        return false;
                    default:
                        // BudgetExceeded/Unknown is neither "hidden" nor permission to loot through a known voxel.
                        SavePending();
                        _retrySightNextStep = body.State == Sight.Later;
                        ProbeTrace.Note(e, $"sight: {nextSample} unanswered; completed voxel hits retained, no take and no hidden cache");
                        return false;
                }
            }
        }

        void SavePending()
        {
            if (!p.PendingSight.ContainsKey(key) && p.PendingSight.Count >= 2048) p.PendingSight.Clear();
            p.PendingSight[key] = new(pendingUntil, trace, nextSample, rays);
        }

        void Remember(bool visible)
        {
            p.PendingSight.Remove(key);
            if (p.Sight.Count > 2048) p.Sight.Clear();
            p.Sight[key] = (now + SightCacheMs, visible, trace);
        }
    }

    /// <summary>Line of sight answered "hidden" for this target lately (InSight false for another reason: the step's
    /// ray budget is spent, ask again).</summary>
    private bool HiddenNow(PlayerLoot p, ulong key) =>
        p.Sight.TryGetValue(key, out var c) && !c.Visible && c.Until > _clock.NowMs;

    private void LeftBehind(PlayerLoot p, uint item)
    {
        if (!p.Notices.ShouldNotify(item, p.Settings.SkipNotice)) return;   // at most 3 per item, 10 s apart
        _notice(p.Entity, new NoticeMsg(NoticeKind.LeftBehind, item));
    }

    private void InventoryFull(PlayerLoot p) => _notice(p.Entity, new NoticeMsg(NoticeKind.InventoryFull, 0));

    /// <summary>Template of an entity, cached by entity and its spawn serial. The key combines both, so a reused entity id
    /// never continues the old entity's visit or pickup episode.</summary>
    private (ulong Key, TemplateRule? Rule)? Classify(EntityId e)
    {
        // the game hands a freed id to the next new entity (a corpse, a drop): a SpawnTime that changed is another one
        var stamp = _game.SpawnStamp(e);
        if (_templates.TryGetValue(e, out var c) && c.Stamp == stamp)
        {
            _templates[e] = (c.Serial, c.Rule, _scanNumber, c.Template, c.Stamp);
            return (KeyOf(e, c.Serial), c.Rule);
        }
        var inspectStart = _micros();
        var inspected = _game.Inspect(e);
        var took = _micros() - inspectStart;
        _step.New++;
        _step.InspectMicros += took;
        if (took >= _step.SlowestInspect)
        {
            _step.SlowestInspect = took;
            _step.SlowestEntity = e;
        }
        if (inspected is not { } info)
        {
            _templates.Remove(e);
            return null;
        }
        TemplateRule? rule = info.Template is { } g && _data.Templates.TryGet(g, out var r) ? r : null;
        // Kiln's serial names what Kiln spawned; anything else is told apart by its SpawnTime (per-target state is keyed
        // by both, so a new entity under an old id starts afresh)
        var serial = info.Serial != 0 ? info.Serial : (ulong)(stamp ^ (stamp >> 32));
        _templates[e] = (serial, rule, _scanNumber, info.Template, stamp);
        return (KeyOf(e, serial), rule);
    }

    /// <summary>"/autoloot near": the entities of <paramref name="report"/> are watched through the next whole scan, then
    /// it gets the loop's notes and its view of each (tick thread). A report armed before replaces this one.</summary>
    public void Explain(NearReport report)
    {
        if (_near is { } old) StopNear(old);
        report.Arm(ProbeTrace.Watching, ProbeTrace.Sink);
        ProbeTrace.Watching = report.Watches;
        ProbeTrace.Sink = report.Note;
        _near = report;
    }

    private void FinishNear(NearReport report)
    {
        StopNear(report);
        report.Finish(this, _game, _data);
    }

    private void StopNear(NearReport report)
    {
        (ProbeTrace.Watching, ProbeTrace.Sink) = report.Restore();
        _near = null;
    }

    /// <summary>The template and SpawnTime the loop has cached for an entity (null: none; "/autoloot near").</summary>
    internal (Guid? Template, long Stamp)? Cached(EntityId e) => _templates.TryGetValue(e, out var c) ? (c.Template, c.Stamp) : null;

    /// <summary>The template behind an entity <see cref="Classify"/> looked at in this scan, or null.</summary>
    private Guid? TemplateOf(EntityId e) => _templates.TryGetValue(e, out var c) ? c.Template : null;

    /// <summary>Entities no player saw in this scan are inspected again when they come back (their id may be reused).</summary>
    private void ForgetUnseenTemplates()
    {
        List<EntityId>? gone = null;
        foreach (var (e, c) in _templates)
            if (c.Scan != _scanNumber) (gone ??= []).Add(e);
        if (gone is not null) foreach (var e in gone) _templates.Remove(e);
    }

    internal static ulong KeyOf(EntityId e, ulong serial) => e.Value | ((serial & 0xffffffffUL) << 32);

    private static float DistanceSquared(WorldPosition a, WorldPosition b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)(dx * dx + dy * dy + dz * dz);
    }
}
