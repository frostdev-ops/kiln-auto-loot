// SPDX-License-Identifier: MIT
// Salvage requests, one item at a time: check it again, see that its runes fit, write the ledger entry and the item's
// ticket, take the item (the game destroys gear), give the runes. Runes that do not fit after the take stay owed in the
// ledger and are paid later (PayOwed). Every Kiln call goes through ISalvageWorld, so tests/Offline drives the whole
// flow with a fake. Tick thread.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;

namespace KilnAutoLoot.Server;

/// <summary>One item as the server reads it now. Holder: whose inventory (the player).</summary>
internal readonly record struct SalvageItem(EntityId Holder, ulong Slot, EntityId ItemEntity, uint Item, int Level, int Rarity,
    SalvageBlock Block, int Runes, string Name);

internal interface ISalvageWorld
{
    long NowMs { get; }
    /// <summary>The item as it is now in the player's backpack, checked (Block), with its runes; null when it is not there.</summary>
    SalvageItem? Read(EntityId player, EntityId itemEntity);
    ulong SteamIdOf(EntityId player);
    /// <summary>The rune item (BalancingTable.perkCostItem); 0 = unknown.</summary>
    uint RuneItem { get; }
    /// <summary>Writes (or with null removes) the ticket on the item's own saved data. False: it could not.</summary>
    bool SetTicket(EntityId itemEntity, string? ticket);
    void FitsGive(EntityId holder, uint item, int count, Action<bool> fits);
    void Take(SalvageItem item, Action<bool> done);
    void Give(EntityId holder, uint item, int count, Action<bool> done);
    void SaveLedger(SalvageLedger ledger);
    void Log(string line);
}

internal sealed class SalvageJobs(ISalvageWorld world, SalvageLedger ledger)
{
    private sealed class Batch(EntityId player, Queue<EntityId> items, Action<SalvageOutcome>? done)
    {
        public EntityId Player { get; } = player;
        public Queue<EntityId> Items { get; } = items;
        public Action<SalvageOutcome>? Done { get; } = done;
        public int Salvaged, Runes, Skipped;
        public SalvageBlock Reason;

        public void Skip(SalvageBlock why)
        {
            Skipped++;
            if (Reason == SalvageBlock.None) Reason = why;
        }
    }

    private readonly Queue<Batch> _batches = new();
    private Batch? _current;
    private bool _inFlight;
    private readonly HashSet<ulong> _paying = [];
    private readonly Dictionary<ulong, int> _payingSteam = new();

    public SalvageLedger Ledger => ledger;

    /// <summary>A request is running or waiting for this player.</summary>
    public bool Busy(EntityId player) => _current?.Player == player || _batches.Any(b => b.Player == player);

    public void Enqueue(EntityId player, IEnumerable<EntityId> items, Action<SalvageOutcome>? done)
    {
        var q = new Queue<EntityId>(items.Distinct());
        _batches.Enqueue(new Batch(player, q, done));
        Pump();
    }

    /// <summary>Moves the work on: the next item when none is in flight. Called after every answer and each tick.</summary>
    public void Pump()
    {
        while (!_inFlight)
        {
            if (_current is null)
            {
                if (!_batches.TryDequeue(out _current)) return;
            }
            if (!_current.Items.TryDequeue(out var entity))
            {
                var b = _current;
                _current = null;
                b.Done?.Invoke(new SalvageOutcome(b.Salvaged, b.Runes, b.Skipped, b.Reason));
                continue;
            }
            Start(_current, entity);
        }
    }

    private void Start(Batch b, EntityId entity)
    {
        if (world.Read(b.Player, entity) is not { } item)
        {
            b.Skip(SalvageBlock.Changed);
            return;
        }
        if (item.Block != SalvageBlock.None)
        {
            b.Skip(item.Block);
            return;
        }
        var rune = world.RuneItem;
        if (rune == 0)
        {
            b.Skip(SalvageBlock.NoYield);
            return;
        }
        _inFlight = true;
        world.FitsGive(b.Player, rune, item.Runes, fits => Guard(b, () =>
        {
            if (!fits)
            {
                // no room for this one's runes: the rest of the request would not fit either
                b.Skip(SalvageBlock.NoRoom);
                while (b.Items.TryDequeue(out _)) b.Skip(SalvageBlock.NoRoom);
                Next();
                return;
            }
            TakeAndGive(b, item, rune);
        }));
    }

    /// <summary>A callback that throws must not stop the queue: the item is skipped (a ledger entry it left is settled
    /// as usual: Taking after a load, Owed by PayOwed) and the next one starts.</summary>
    private void Guard(Batch b, Action body)
    {
        var step = _step;
        try { body(); }
        catch (Exception e)
        {
            if (step != _step)
            {
                // the item was done and a later one failed while it started: that one is gone from the queue
                world.Log($"salvage: starting the next item failed ({e.GetType().Name}: {e.Message}); it is skipped");
                if (_current is { } now) now.Skip(SalvageBlock.Changed);
                if (_inFlight) return;
                Next();
                return;
            }
            world.Log($"salvage: a step failed ({e.GetType().Name}: {e.Message}); the item is skipped");
            b.Skip(SalvageBlock.Changed);
            Next();
        }
    }

    private int _step;

    private void TakeAndGive(Batch b, SalvageItem item, uint rune)
    {
        var steam = world.SteamIdOf(b.Player);
        if (steam == 0)
        {
            // owed runes could not find their player again
            b.Skip(SalvageBlock.NoAccount);
            Next();
            return;
        }
        var ticket = ledger.Begin(steam, item.Name, rune, item.Runes, world.NowMs);
        if (!world.SetTicket(item.ItemEntity, ticket))
        {
            ledger.TakeFailed(ticket);
            b.Skip(SalvageBlock.Changed);
            Next();
            return;
        }
        world.SaveLedger(ledger);
        world.Take(item, taken => Guard(b, () =>
        {
            if (!taken)
            {
                ledger.TakeFailed(ticket);
                world.SetTicket(item.ItemEntity, null);   // the item is still there: no ticket on it
                world.SaveLedger(ledger);
                b.Skip(SalvageBlock.Changed);
                Next();
                return;
            }
            ledger.Taken(ticket);
            world.SaveLedger(ledger);
            b.Salvaged++;
            world.Give(b.Player, rune, item.Runes, given => Guard(b, () =>
            {
                if (given)
                {
                    ledger.Paid(ticket);
                    world.SaveLedger(ledger);
                    b.Runes += item.Runes;
                    world.Log($"salvage: player {b.Player} salvaged {item.Name} (level {item.Level}, rarity {item.Rarity}) -> {item.Runes} runes");
                }
                else world.Log($"salvage: player {b.Player} salvaged {item.Name} (level {item.Level}, rarity {item.Rarity}); " +
                               $"{item.Runes} runes did not fit and are owed (ticket {ticket})");
                Next();
            }));
        }));
    }

    private void Next()
    {
        _step++;
        _inFlight = false;
        Pump();
    }

    /// <summary>A payment of owed runes is running for this player (the Trash check waits for it).</summary>
    public bool Paying(EntityId player) => _payingSteam.TryGetValue(world.SteamIdOf(player), out var n) && n > 0;

    /// <summary>Runes owed to this (online) player: given now, entry by entry; each runs once at a time.</summary>
    public void PayOwed(EntityId player)
    {
        var steam = world.SteamIdOf(player);
        if (steam == 0) return;
        foreach (var e in ledger.OwedTo(steam).ToList())
        {
            var key = (ulong)e.Ticket.GetHashCode() ^ steam;
            if (!_paying.Add(key)) continue;
            _payingSteam[steam] = _payingSteam.GetValueOrDefault(steam) + 1;
            var ticket = e.Ticket;
            world.Give(player, e.RuneItem, e.Runes, given =>
            {
                _paying.Remove(key);
                _payingSteam[steam]--;
                if (!given || ledger.Find(ticket) is not { State: LedgerState.Owed } now) return;
                ledger.Paid(ticket);
                world.SaveLedger(ledger);
                world.Log($"salvage: player {player} got {now.Runes} owed runes for {now.Item}");
            });
        }
    }

    public int OwedRunes(EntityId player) => ledger.OwedTo(world.SteamIdOf(player)).Sum(e => e.Runes);

    /// <summary>The world stops: requests that did not start are dropped (the ledger keeps what did).</summary>
    public void Reset()
    {
        _batches.Clear();
        _current = null;
        _inFlight = false;
        _paying.Clear();
        _payingSteam.Clear();
    }
}
