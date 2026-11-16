// SPDX-License-Identifier: MIT
// Kiln Auto Loot's loot loop on the server, per player. Every game call goes through IGame (tests/Offline/Server fakes
// it). Tick thread only.
//
// One scan = every player once, every 100 ms by wall clock. Each scan evaluates every target in range
// afresh (at least every 2 s, so a corpse that was empty when it died is looked at again): nothing is ever "done" except through the
// visit table. At most 64 injects (accept, loot-all, Items.Move) per scan for the whole mod; the rest waits.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;

namespace KilnAutoLoot.Server;

/// <summary>A command's outcome: the focus after it, and whether to answer with a ServerInfoMsg.</summary>
internal readonly record struct CommandResult(uint Focus, bool Reply);

internal sealed class LootLoop
{
    public const long ScanEveryMs = 100;           // each player every 100 ms
    public const int MaxInjectsPerScan = 64;       // for the whole mod
    public const long BlinkMs = 2000;              // the 2 s re-evaluation (counted only)
    public const long SightCacheMs = 1000;         // about 1 s per target
    public const long StallRetryMs = 1000;         // a container the full player cannot take from
    public const float StaticPickupNearSquared = 0.5625f;   // the game's own 0.75 m, squared
    public const long SettingsGapMs = 500;         // one settings application per player per 500 ms
    public const int SettingsPerTick = 2;          // and at most two per tick for all players; the rest wait a tick
    public const int CommandsPerSecond = 4;        // gather focus commands per player per second; the rest are dropped
    public const long HandTakeWindowMs = 2000;     // a hand take counts when its item arrives within this time

    // Candidate components, one World.Find each per scan, shared by all players.
    public const string PickupComponent = "PickupItem";
    public const string OfferComponent = "InteractionOfferRuntime";
    public const string InventoryComponent = "Inventory";
    public const string LootMarkerComponent = "DisableColliderModeChanges";
    public const string EnemyComponent = "EnemySettings";

    private readonly IGame _game;
    private readonly IClock _clock;
    private readonly Data _data;
    private readonly Action<EntityId, NoticeMsg> _notice;
    private readonly Action<string> _keyLine;
    private readonly Dictionary<EntityId, PlayerLoot> _players = new();
    private readonly Dictionary<EntityId, (ulong Serial, TemplateRule? Rule, long Scan)> _templates = new();
    private readonly HashSet<EntityId> _playerIds = [];
    private readonly Dictionary<string, IgnoreList> _ignoreCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignoreReported = new(StringComparer.Ordinal);
    private int _ignoreVersion = -1;
    private ServerSettings _server = new();
    private PersonalSettings _defaults = new();
    private long _lastScan = long.MinValue, _lastBlink, _scanNumber;
    private int _budget, _rotate;

    public LootCounters Counters { get; } = new();

    /// <param name="notice">sends a notice to a player's game (or hands it to the hosting game's client part).</param>
    /// <param name="keyLine">key log lines: unknown ignore entries.</param>
    public LootLoop(IGame game, IClock clock, Data data, Action<EntityId, NoticeMsg> notice, Action<string> keyLine)
    {
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
    /// the target is no pickup or goes down the container path (a swarm).</summary>
    public uint? HandPickupItem(EntityId host)
    {
        if (!_game.TryPickupItem(host, out var item)) return null;
        if (Classify(host) is { Rule: { } rule } && !Release.IsPickupPath(rule)) return null;
        return item;
    }

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
        _lastScan = long.MinValue;
    }

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

    /// <summary>Every server step: scans when 100 ms passed since the last scan.</summary>
    public void Step()
    {
        var now = _clock.NowMs;
        if (_lastScan != long.MinValue && now - _lastScan < ScanEveryMs) return;
        _lastScan = now;
        Scan();
    }

    /// <summary>One scan of every player.</summary>
    public void Scan()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var now = _clock.NowMs;
        _scanNumber++;
        _budget = MaxInjectsPerScan;
        if (_ignoreVersion != _data.Items.Version)   // the item table was filled again: match every skip list anew
            foreach (var player in _players.Values) Recompute(player);
        Counters.Scans++;
        if (now - _lastBlink >= BlinkMs)
        {
            _lastBlink = now;
            Counters.Rechecks++;
        }

        var players = _game.Players();
        var lists = new Candidates(_game);
        _playerIds.Clear();
        foreach (var gp in players) _playerIds.Add(gp.Entity);
        for (var i = 0; i < players.Count; i++)
        {
            // fairness under the cap: a different player goes first each scan
            var gp = players[(i + _rotate) % players.Count];
            ScanPlayer(Join(gp.Entity), gp, lists);
        }
        if (players.Count > 0) _rotate = (_rotate + 1) % players.Count;
        ForgetUnseenTemplates();

        var micros = System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks / 10;
        Counters.ScanMicros += micros;
        Counters.ScanCount++;
        if (micros > Counters.ScanMaxMicros) Counters.ScanMaxMicros = micros;
    }

    /// <summary>The shared World.Find lists of one scan, each fetched on first need.</summary>
    private sealed class Candidates(IGame game)
    {
        private IReadOnlyList<EntityId>? _pickups, _offers, _inventories, _markers, _enemies;
        public IReadOnlyList<EntityId> Pickups => _pickups ??= game.Find(PickupComponent);
        public IReadOnlyList<EntityId> Offers => _offers ??= game.Find(OfferComponent);
        public IReadOnlyList<EntityId> Inventories => _inventories ??= game.Find(InventoryComponent);
        public IReadOnlyList<EntityId> Markers => _markers ??= game.Find(LootMarkerComponent);
        public IReadOnlyList<EntityId> Enemies => _enemies ??= game.Find(EnemyComponent);
    }

    private void ScanPlayer(PlayerLoot p, GamePlayer gp, Candidates lists)
    {
        var s = p.Settings;
        Counters.PlayerScans++;
        // nothing until the player's own settings are here (or the wait for them is over)
        p.FirstSeenMs ??= _clock.NowMs;
        if (!Settled(p, _clock.NowMs))
        {
            Counters.SettingsWaits++;
            return;
        }
        // a game window or the F8 window is open: no accept, no loot-all for this player.
        var window = p.ReportsWindow ? p.WindowOpen : gp.UiOpen;
        if (window)
        {
            Counters.WindowPauses++;
            return;
        }
        if (!s.Looting) return;   // only what the player takes by hand (E)

        // the mod's own search, exactly enemyDistance, not capped by the loot radius.
        if (s.EnemyPause)
            foreach (var e in _game.Within(lists.Enemies, gp.Position, s.EnemyDistance))
                if (Classify(e) is { Rule: { } er } && Release.IsPausingEnemy(er, _game.IsDead(e)))
                {
                    p.Enemy.EnemySeen();
                    Counters.EnemyNear++;
                    break;
                }
        if (p.Enemy.Paused(s.EnemyPause))
        {
            Counters.EnemyPauses++;
            return;
        }
        Counters.PlayersLooting++;

        var radius = LootRules.SearchRadius(s);   // the largest of the general and the group radii
        var seen = new HashSet<EntityId>();
        foreach (var list in new Func<IReadOnlyList<EntityId>>[] { () => lists.Pickups, () => lists.Offers, () => lists.Inventories, () => lists.Markers })
            foreach (var e in _game.Within(list(), gp.Position, radius))
                if (!_playerIds.Contains(e) && seen.Add(e))
                    Target(p, gp, e);
    }

    private void Target(PlayerLoot p, GamePlayer gp, EntityId e)
    {
        if (_game.PositionOf(e) is not { } at) return;
        if (!HeightWindow.Allows(at.Y - gp.Position.Y, p.Settings))
        {
            ProbeTrace.Note(e, $"height: outside the height window (dy {at.Y - gp.Position.Y:0.0} m)");
            Counters.HeightOut++;   // above or below the player's height window: as if out of range
            return;
        }
        var d2 = DistanceSquared(gp.Position, at);
        if (Classify(e) is not { Rule: { } rule, Key: var key })
        {
            ProbeTrace.Note(e, "table: not in the template table (never taken)");
            Counters.UnknownTemplates++;   // a template not in templates.txt: never
            return;
        }
        var hasPickup = _game.TryPickupItem(e, out var item);
        var container = Release.IsContainerTarget(rule, hasPickup);
        ProbeTrace.Note(e, $"seen: {rule.Group} {rule.Flags}, {(container ? "container path" : $"pickup path, item {item}")}");
        if (container) Counters.ContainersSeen++;
        else Counters.PickupsSeen++;
        if (container) ContainerTarget(p, gp, e, key, rule, d2);
        else PickupTarget(p, gp, e, key, rule, d2, item);
    }

    // ---------------------------------------------------------------- container path

    private void ContainerTarget(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, float d2)
    {
        var s = p.Settings;
        var dead = rule.Group == Group.Enemy && _game.IsDead(e);
        if (rule.Group == Group.Enemy && !dead) return;   // a living enemy: never looted (gate step 3)
        if (!Release.ContainerReleased(rule, dead))
        {
            ProbeTrace.Note(e, "container: not released");
            Counters.ContainersHeldBack++;   // gate step 4
            return;
        }
        var verdict = LootRules.Decide(rule, released: true, d2, s, p.Filter, out var noticeItem);
        if (verdict != Verdict.Allowed)
        {
            ProbeTrace.Note(e, $"container: refused ({verdict})");
            CountRefusal(p, verdict, noticeItem);
            return;
        }
        if (Release.IsHarvest(rule) && !HarvestFits(p, gp, e, rule, out _))
        {
            ProbeTrace.Note(e, "container: the harvest does not fit the bag rules");
            return;   // checked before the accept too
        }
        if (!InSight(p, gp, e, key))
        {
            ProbeTrace.Note(e, "container: not in sight");
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
        if (p.PendingMoves.ContainsKey(key)) return;   // the last moves have not answered yet
        var s = p.Settings;
        if (Release.IsHarvest(rule))
        {
            // plants and crops: the loot-all is a harvest, taken whole; the item filter judged its yield before, the bag
            // judges its live contents now
            if (!HarvestFits(p, gp, e, rule, out var contents)) return;
            if (SendLoot(p, gp, e, key, offer) && contents is not null)
                foreach (var (item, count) in contents) p.Bag?.Took(item, count);
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
            TakeUnread(p, gp, e, key, rule, offer);
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
        foreach (var slot in slots)
            switch (LootRules.Slot(slot.Item, slot.Rarity, s, _data.Quest, p.Filter, _data.Items))   // quest, filter, rarity, take
            {
                case SlotRule.KeepQuest:
                    Counters.QuestKept++;
                    kept = true;
                    break;
                case SlotRule.KeepIgnored:
                    Counters.SkippedInContainers++;
                    kept = true;
                    LeftBehind(p, slot.Item);
                    break;
                case SlotRule.KeepNotListed:
                    Counters.NotListed++;
                    kept = true;
                    break;
                case SlotRule.KeepNotFocus:
                    Counters.NotFocus++;
                    kept = true;
                    break;
                case SlotRule.KeepRarity:
                    Counters.RarityLow++;
                    kept = true;
                    break;
                default:
                    var allowed = BagRules.Allowance(slot.Item, s, plan, p.Limits, out var why);
                    if (allowed <= 0)
                    {
                        CountBag(why == BagRefusal.None ? BagRefusal.NoRoom : why);
                        kept = true;
                        break;
                    }
                    var count = allowed >= slot.Count ? 0 : allowed;   // 0 = the whole slot
                    if (count > 0) kept = true;                         // part of the slot stays
                    var moving = count == 0 ? slot.Count : count;
                    plan?.Took(slot.Item, moving);
                    take.Add((slot, count));
                    takeable += moving;
                    break;
            }

        ProbeTrace.Note(e, $"loot: {take.Count} slots to take, something kept: {kept}");
        if (take.Count == 0)
        {
            // nothing to take: corpses and chests stay as they are; others still get the loot-all (harvest-like), a
            // creature's pile as when it cannot be read (its default inventory comes with the loot-all)
            if (kept || Release.SkipWhenEmpty(rule)) return;
            if (Release.IsDrop(rule)) TakeUnread(p, gp, e, key, rule, offer);
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
            Counters.CountLooted(slot.Item, moving);
            p.Bag?.Took(slot.Item, moving);
            p.PendingMoves[key] = p.PendingMoves.GetValueOrDefault(key) + 1;
            var item = slot.Item;
            p.MovesInFlight[item] = p.MovesInFlight.GetValueOrDefault(item) + moving;
            ProbeTrace.Note(e, $"send: move slot {slot.Id} ({item} x{moving})");
            _game.Move(e, slot.Id, gp.Entity, count, outcome => MoveAnswered(p, key, outcome, item, moving));
        }
        if (sent > 0)
        {
            CountLootKind(offer);
            // Ground and creature-pile slot moves can leave a remainder or fail: only their loot-all finishes the visit
            // (they are harvested once per visit, Release.HarvestOnce).
            if (rule.Group != Group.Ground && !Release.IsDrop(rule)) p.Visits.Looted(key);
        }
    }

    private void MoveAnswered(PlayerLoot p, ulong key, MoveOutcome outcome, uint item, int count)
    {
        if (p.MovesInFlight.TryGetValue(item, out var flying))
        {
            if (flying <= count) p.MovesInFlight.Remove(item);
            else p.MovesInFlight[item] = flying - count;
        }
        if (p.PendingMoves.TryGetValue(key, out var n))
        {
            if (n <= 1) p.PendingMoves.Remove(key);
            else p.PendingMoves[key] = n - 1;
        }
        if (outcome == MoveOutcome.NoRoom && p.Full.MayNotify()) InventoryFull(p);
    }

    /// <summary>The loot-all of a target whose contents cannot be seen (a fog-nest crystal, a creature's pile): taken whole.
    /// The crystal's yield was judged by LootRules.Decide. A pile's is judged here, as it comes whole: in skip mode one
    /// whose yield holds a skipped item stays; a picking filter (only list, gather focus) needs a yield item it takes
    /// (Decide lets a corpse without a known yield through for its slots, which cannot be seen here). With a bag rule on
    /// the live contents have to fit (HarvestFits).</summary>
    private void TakeUnread(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, Offer offer)
    {
        if (Release.IsDrop(rule))
        {
            if (p.Filter.Picks)
            {
                if (!rule.Yield.Any(p.Filter.Allows))
                {
                    ProbeTrace.Note(e, "loot: the pile gives nothing the filter takes");
                    if (p.Filter.Focus != 0) Counters.NotFocus++;
                    else Counters.NotListed++;
                    return;
                }
            }
            else if (LootRules.FirstIgnoredYield(rule, p.Filter.Skip) is var skipped and not 0)
            {
                ProbeTrace.Note(e, $"loot: the pile's yield holds skipped item {skipped}");
                Counters.YieldSkipped++;
                LeftBehind(p, skipped);
                return;
            }
        }
        if (HarvestFits(p, gp, e, rule, out _)) SendLoot(p, gp, e, key, offer);
    }

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

    private void PickupTarget(PlayerLoot p, GamePlayer gp, EntityId e, ulong key, TemplateRule rule, float d2, uint item)
    {
        var s = p.Settings;
        var verdict = LootRules.Decide(rule, released: false, d2, s, p.Filter, out var noticeItem);
        var wanted = verdict == Verdict.Allowed;
        if (!wanted)
        {
            ProbeTrace.Note(e, $"pickup: refused ({verdict})");
            CountRefusal(p, verdict, noticeItem);
        }
        var details = _game.PickupDetails(e) ?? new GroundItem(1, GearRules.UnknownRarity);
        if (wanted)   // the pickup's own item: the item filter, then the rarity rule
            switch (p.Filter.Check(item))
            {
                case ItemRefusal.Skipped:
                    wanted = false;
                    Counters.SkippedPickups++;
                    LeftBehind(p, item);
                    break;
                case ItemRefusal.NotListed:
                    wanted = false;
                    Counters.NotListed++;
                    break;
                case ItemRefusal.NotFocus:
                    wanted = false;
                    Counters.NotFocus++;
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
            CountBag(BagRules.Pickup(item, details.Amount, s, BagOf(p, gp), p.Limits))) wanted = false;
        var withInventory = _game.HasInventory(e);
        ProbeTrace.Note(e, $"pickup: wanted after the item, rarity and bag rules: {wanted} (amount {details.Amount}, " +
                           $"rarity {details.Rarity}, own inventory {withInventory})");
        // takeInPlace off: static pickups (with an inventory) cannot be pulled: only within 0.75 m.
        if (wanted && withInventory && !s.TakeInPlace && d2 > StaticPickupNearSquared) wanted = false;
        if (wanted && !InSight(p, gp, e, key)) wanted = false;
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
    /// exact items and amounts, and all of them have to fit; one that cannot be read, reads empty or does not fit is
    /// left (counted). Without a bag rule nothing is read. <paramref name="contents"/>: what to reserve when it goes.</summary>
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
            Counters.HarvestUnread++;   // the amounts are not known: a bag rule cannot be kept
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

    /// <summary>Line of sight: only with visibleOnly; cached about 1 s per target; "not known" = visible.</summary>
    private bool InSight(PlayerLoot p, GamePlayer gp, EntityId e, ulong key)
    {
        if (!p.Settings.VisibleOnly) return true;
        var now = _clock.NowMs;
        if (p.Sight.TryGetValue(key, out var c) && c.Until > now) return c.Visible;
        switch (_game.LineOfSight(gp.Entity, e))
        {
            case Sight.Later: return false;
            case Sight.Hidden:
                Counters.SightBlocked++;
                Remember(false);
                return false;
            default:
                Remember(true);
                return true;
        }

        void Remember(bool visible)
        {
            if (p.Sight.Count > 2048) p.Sight.Clear();
            p.Sight[key] = (now + SightCacheMs, visible);
        }
    }

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
        if (_templates.TryGetValue(e, out var c))
        {
            _templates[e] = (c.Serial, c.Rule, _scanNumber);
            return (KeyOf(e, c.Serial), c.Rule);
        }
        if (_game.Inspect(e) is not { } info) return null;
        TemplateRule? rule = info.Template is { } g && _data.Templates.TryGet(g, out var r) ? r : null;
        _templates[e] = (info.Serial, rule, _scanNumber);
        return (KeyOf(e, info.Serial), rule);
    }

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
