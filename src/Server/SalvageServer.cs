// SPDX-License-Identifier: MIT
// The server world's salvage: the players' commands (list, mark, keep, salvage), the rules for gear that arrives from
// the world, the ledger's load and its owed runes, and the calibration against the game's own Trash. Kiln glue only;
// the flow is SalvageJobs, the rules src/Core/Salvage.cs, the yield's game data SalvageData. Tick thread.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;

// kiln.players and kiln.spawn: LootServer starts salvage only where its looting runs, which needs both
#pragma warning disable KILN121

namespace KilnAutoLoot.Server;

/// <summary>What the mod keeps on one item (InstanceData, saved with the world): marked, kept, the ticket of a salvage
/// in flight.</summary>
public sealed class SalvageMark
{
    public bool Marked { get; set; }
    public bool Kept { get; set; }
    public string? Ticket { get; set; }
}

internal sealed class SalvageServer
{
    private const string LedgerKey = "salvage-ledger";
    private readonly Data _data;
    private readonly Func<EntityId, PersonalSettings> _settingsOf;
    private readonly Func<ServerLimits?> _limits;
    private readonly Action<EntityId, SalvageStateMsg> _send;
    private readonly Action<string> _log;
    private readonly SalvageSources _sources;
    private readonly IClock _clock;
    private readonly Dictionary<EntityId, SalvageOutcome> _last = new();
    private readonly List<(EntityId Player, EntityId Item, int Predicted, int Before, long DueMs, string Name)> _checks = [];
    private InstanceKey<SalvageMark>? _key;
    private KilnSalvageWorld? _world;
    private SalvageJobs? _jobs;
    private bool _ledgerLoaded, _resolved, _mismatch, _ledgerBad;
    private long _loadedAtMs, _nextOwedMs;

    public SalvageServer(Data data, IClock clock, Func<EntityId, PersonalSettings> settingsOf, Func<ServerLimits?> limits,
        Action<EntityId, SalvageStateMsg> send, Action<string> log)
    {
        _data = data;
        _clock = clock;
        _settingsOf = settingsOf;
        _limits = limits;
        _send = send;
        _log = log;
        _sources = new SalvageSources(clock);
    }

    /// <summary>At the world's start. The key is declared once per process.</summary>
    public void StartWorld()
    {
        try { _key ??= InstanceData.Key<SalvageMark>("salvage", new InstanceKeyOptions { Persist = Persist.World }); }
        catch (Exception e) { _log($"salvage: no item data ({e.Message}): salvage is off in this world"); }
        _world = new KilnSalvageWorld(_data, _clock, _key, _log);
        _jobs = new SalvageJobs(_world, new SalvageLedger());
        _ledgerLoaded = _resolved = _mismatch = _ledgerBad = false;
        _checks.Clear();
        _last.Clear();
        var b = _world.Balancing;
        _log(b is null ? "salvage: the game's BalancingTable could not be read: salvage is off in this world"
            : $"salvage: yield data seed {b.Seed}, creep {b.Creep}, trash {b.TrashStart}..{b.TrashEnd}, factors {b.WeaponsShield}/{b.Armor}/{b.Rings}, rune item {_world.RuneItem}");
    }

    public void StopWorld()
    {
        if (_world is not null) _world.Stopped = true;
        _jobs?.Reset();
        _jobs = null;
        _world = null;
    }

    private SalvageReady Ready()
    {
        if (_limits() is { AllowSalvage: false }) return SalvageReady.Off;
        if (_key is null || _world is null || !Saves.WorldDataReady || !Capabilities.Is("kiln.items") ||
            !Capabilities.Is("saves.instance_data")) return SalvageReady.NoItemData;   // instance data implies items.instances
        if (_ledgerBad) return SalvageReady.NoItemData;
        if (!_resolved) return SalvageReady.Starting;   // the saved ledger is loaded and settled first
        if (_world.Balancing is null || _world.RuneItem == 0) return SalvageReady.NoGameData;
        if (_mismatch) return SalvageReady.Mismatch;
        return SalvageReady.Ready;
    }

    // ---------------------------------------------------------------- tick

    public void Tick()
    {
        if (_jobs is null || _world is null) return;
        if (!_ledgerLoaded && Saves.WorldDataReady)
        {
            _ledgerLoaded = true;
            _loadedAtMs = _clock.NowMs;
            SalvageLedger? saved = null;
            try { saved = Kiln.WorldData.TryGet<SalvageLedger>(LedgerKey, out var l) ? l : null; }
            catch (Exception e)
            {
                // never overwrite it with an empty one: its owed runes would be lost
                _ledgerBad = true;
                _log($"salvage: the saved ledger does not read ({e.Message}); salvage stays off in this world");
            }
            if (saved is not null && saved.Entries.Count > 0)
            {
                _jobs = new SalvageJobs(_world, saved);
                _log($"salvage: ledger has {saved.Entries.Count} open salvage(s) from the last session");
            }
        }
        // the takes in flight at the last save: resolved once the saved item records are back (10 s after the load)
        if (_ledgerLoaded && !_resolved && _clock.NowMs - _loadedAtMs >= 10_000)
        {
            _resolved = true;
            if (_jobs.Ledger.Entries.Any(e => e.State == LedgerState.Taking))
            {
                var (dropped, owed) = _jobs.Ledger.ResolveAfterLoad(TicketExists);
                _world.SaveLedger(_jobs.Ledger);
                _log($"salvage: after the load {dropped} salvage(s) kept their item (nothing owed), {owed} lost it (runes owed)");
            }
        }
        _jobs.Pump();
        if (_resolved && _clock.NowMs >= _nextOwedMs && _jobs.Ledger.Entries.Count > 0)
        {
            _nextOwedMs = _clock.NowMs + 5000;
            foreach (var p in Kiln.Players.All)
                if (p.State == PlayerState.InGame) _jobs.PayOwed(p.Entity);
        }
        RunChecks();
    }

    private bool TicketExists(string ticket)
    {
        if (_key is null) return true;   // cannot tell: keep the item's side (no duplicate)
        try
        {
            foreach (var (_, mark) in InstanceData.All(_key))
                if (mark?.Ticket == ticket) return true;
            foreach (var parked in InstanceData.Parked(_key))
                if (parked.Value?.Ticket == ticket) return true;
            return false;
        }
        catch (Exception e)
        {
            _log($"salvage: could not look for ticket {ticket} ({e.Message}); keeping the item's side");
            return true;
        }
    }

    // ---------------------------------------------------------------- commands

    public void Command(EntityId player, SalvageCmd kind, IReadOnlyList<ulong> tokens)
    {
        if (_world is null || _jobs is null) return;
        var ready = Ready();
        if (ready == SalvageReady.Ready || kind == SalvageCmd.List)
        {
            var items = tokens.Select(t => new EntityId((uint)t)).ToList();
            switch (kind)
            {
                case SalvageCmd.Mark: _world.Change(player, items, m => { if (!m.Kept) m.Marked = true; }); break;
                case SalvageCmd.Unmark: _world.Change(player, items, m => m.Marked = false); break;
                case SalvageCmd.Keep: _world.Change(player, items, m => { m.Kept = true; m.Marked = false; }); break;
                case SalvageCmd.Unkeep: _world.Change(player, items, m => m.Kept = false); break;
                case SalvageCmd.ClearMarks: _world.ClearMarks(player); break;
                case SalvageCmd.Salvage when ready == SalvageReady.Ready:
                    _jobs.Enqueue(player, items, outcome =>
                    {
                        _last[player] = outcome;
                        SendState(player);
                    });
                    break;
            }
        }
        SendState(player);
    }

    public void SendState(EntityId player)
    {
        if (_world is null || _jobs is null) return;
        var ready = Ready();
        var rows = ready is SalvageReady.NoItemData ? [] : _world.Rows(player);
        _last.TryGetValue(player, out var last);
        _send(player, new SalvageStateMsg(ready, _world.RuneItem, _jobs.OwedRunes(player), rows, last, _jobs.Busy(player)));
    }

    // ---------------------------------------------------------------- gear from the world, rules

    /// <summary>A loot interaction (the player's own E, or Auto Loot's): notes the host as a world source when Auto
    /// Loot's tables know its template (corpses, world chests, ground pickups).</summary>
    public void Looted(EntityId player, EntityId host)
    {
        if (!player.IsPlayer || host.IsNone) return;
        _sources.Looted(player, host, IsWorldSource(host));
    }

    private bool IsWorldSource(EntityId entity) =>
        Spawn.Inspect(entity) is { Template: { } t } && _data.Templates.TryGet(t, out _);

    /// <summary>Items arrived: gear from a world source a player's rules may mark or salvage.</summary>
    public void Arrived(ItemPickupEvent e)
    {
        if (_world is null || _jobs is null || !e.Receiver.IsPlayer || Ready() != SalvageReady.Ready) return;
        if (!_data.Items.TryGet(e.ItemId, out var row) || !GearRules.IsGear(row)) return;
        if (!_sources.FromWorld(e.Receiver, e.Source)) return;
        var s = _settingsOf(e.Receiver);
        var rules = SalvageRules.Parse(s.SalvageRules);
        if (rules.Count == 0) return;
        if (_world.AtSlot(e.Receiver, e.SlotInventory, e.SlotIndex) is not { } item || item.Item != e.ItemId ||
            item.Block != SalvageBlock.None) return;
        var kind = SalvageFormula.KindOf(_world.SlotTypeOf(item.Item));
        if (SalvageRules.FirstMatch(rules, kind, item.Rarity, item.Level) is null) return;
        if (s.SalvageRuleAction == SalvageAction.Salvage)
        {
            _log($"salvage: rule salvages {item.Name} (level {item.Level}, rarity {item.Rarity}) for player {e.Receiver}");
            _jobs.Enqueue(e.Receiver, [item.ItemEntity], outcome => _last[e.Receiver] = outcome);
        }
        else
        {
            _world.Change(e.Receiver, [item.ItemEntity], m => { if (!m.Kept) m.Marked = true; });
            _log($"salvage: rule marks {item.Name} (level {item.Level}, rarity {item.Rarity}) for player {e.Receiver}");
        }
    }

    public void Left(EntityId player)
    {
        _sources.Forget(player);
        _last.Remove(player);
    }

    // ---------------------------------------------------------------- calibration: the game's own Trash

    /// <summary>A player trashes gear by hand: what the formula says is compared with the runes the game gives.</summary>
    public void Tossed(ItemTossEvent e)
    {
        if (_world is null || e.How != DiscardKind.Trash || !e.Player.IsPlayer || e.ItemEntity.IsNone || e.Canceled) return;
        if (_world.Read(e.Player, e.ItemEntity) is not { } item || item.Runes <= 0 || _world.RuneItem == 0) return;
        _checks.Add((e.Player, e.ItemEntity, item.Runes, _world.RuneCount(e.Player), _clock.NowMs + 750, item.Name));
    }

    private void RunChecks()
    {
        if (_world is null || _checks.Count == 0) return;
        var now = _clock.NowMs;
        for (var i = _checks.Count - 1; i >= 0; i--)
        {
            var c = _checks[i];
            if (now < c.DueMs) continue;
            _checks.RemoveAt(i);
            if (_world.Read(c.Player, c.Item) is not null) continue;   // still there: the trash did not run
            if (_jobs is not null && (_jobs.Busy(c.Player) || _jobs.Paying(c.Player))) continue;   // our own runes in the count
            var delta = _world.RuneCount(c.Player) - c.Before;
            if (delta == c.Predicted)
                _log($"salvage check: {c.Name} trashed by hand gave {delta} runes, the formula said {c.Predicted}: match");
            else if (delta > c.Predicted)
                _log($"salvage check: {c.Name} trashed by hand: {delta} runes came in, the formula said {c.Predicted}: unclear " +
                     "(other runes arrived at the same time?)");
            else
            {
                _log($"salvage check: {c.Name} trashed by hand gave {delta} runes, the formula said {c.Predicted}: MISMATCH; " +
                     "salvage stays off in this world. Please report this line.");
                _mismatch = true;
            }
        }
    }
}
