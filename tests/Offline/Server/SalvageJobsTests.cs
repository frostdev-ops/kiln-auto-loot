// SPDX-License-Identifier: MIT
// The salvage flow against a fake world: no runes without the take, no take without room, owed runes when the give
// fails, and the ledger empty after a clean salvage.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

internal sealed class FakeSalvageWorld : ISalvageWorld
{
    public long NowMs { get; set; }
    public readonly Dictionary<EntityId, SalvageItem> Bag = new();
    public readonly Dictionary<EntityId, string?> Tickets = new();
    public int Runes;
    public bool Room = true, TakeWorks = true, GiveWorks = true, TicketWorks = true, GiveThrows;
    public ulong Steam = 1000;
    public int Saves;
    public readonly List<string> Lines = [];
    public uint RuneItem { get; set; } = 77;

    public SalvageItem? Read(EntityId player, EntityId itemEntity) => Bag.TryGetValue(itemEntity, out var i) ? i : null;
    public ulong SteamIdOf(EntityId player) => Steam == 0 ? 0 : Steam + player.Value;
    public bool SetTicket(EntityId itemEntity, string? ticket)
    {
        if (!TicketWorks) return false;
        Tickets[itemEntity] = ticket;
        return true;
    }
    public void FitsGive(EntityId holder, uint item, int count, Action<bool> fits) => fits(Room);
    public void Take(SalvageItem item, Action<bool> done)
    {
        var ok = TakeWorks && Bag.Remove(item.ItemEntity);
        done(ok);
    }
    public void Give(EntityId holder, uint item, int count, Action<bool> done)
    {
        if (GiveThrows) throw new InvalidOperationException("boom");
        if (GiveWorks) Runes += count;
        done(GiveWorks);
    }
    public void SaveLedger(SalvageLedger ledger) => Saves++;
    public void Log(string line) => Lines.Add(line);

    public EntityId Add(uint entity, int runes, SalvageBlock block = SalvageBlock.None)
    {
        var e = new EntityId(entity);
        Bag[e] = new SalvageItem(new EntityId(1), entity, e, 5, 10, 1, block, runes, "Sword " + entity);
        return e;
    }
}

public sealed class SalvageJobsTests
{
    private static readonly EntityId Player = new(1);

    private static (FakeSalvageWorld W, SalvageJobs J, List<SalvageOutcome> Out) Rig()
    {
        var w = new FakeSalvageWorld();
        return (w, new SalvageJobs(w, new SalvageLedger()), []);
    }

    [Fact]
    public void Marked_gear_becomes_its_runes_and_the_ledger_ends_empty()
    {
        var (w, j, outs) = Rig();
        var a = w.Add(10, 9);
        var b = w.Add(11, 20);
        j.Enqueue(Player, [a, b], outs.Add);
        Assert.Equal(new SalvageOutcome(2, 29, 0, SalvageBlock.None), Assert.Single(outs));
        Assert.Empty(w.Bag);
        Assert.Equal(29, w.Runes);
        Assert.Empty(j.Ledger.Entries);
        Assert.Equal(2, w.Lines.Count(l => l.StartsWith("salvage: player")));
    }

    [Fact]
    public void Blocked_and_vanished_items_are_skipped_untouched()
    {
        var (w, j, outs) = Rig();
        var worn = w.Add(10, 9, SalvageBlock.Equipped);
        j.Enqueue(Player, [worn, new EntityId(99)], outs.Add);
        Assert.Equal(new SalvageOutcome(0, 0, 2, SalvageBlock.Equipped), outs[0]);
        Assert.True(w.Bag.ContainsKey(worn));
        Assert.Equal(0, w.Runes);
    }

    [Fact]
    public void No_room_for_the_runes_takes_nothing()
    {
        var (w, j, outs) = Rig();
        w.Room = false;
        var a = w.Add(10, 9);
        var b = w.Add(11, 9);
        j.Enqueue(Player, [a, b], outs.Add);
        Assert.Equal(new SalvageOutcome(0, 0, 2, SalvageBlock.NoRoom), outs[0]);
        Assert.Equal(2, w.Bag.Count);
        Assert.Empty(j.Ledger.Entries);
    }

    [Fact]
    public void A_take_that_fails_gives_no_runes_and_leaves_no_ticket()
    {
        var (w, j, outs) = Rig();
        w.TakeWorks = false;
        var a = w.Add(10, 9);
        j.Enqueue(Player, [a], outs.Add);
        Assert.Equal(0, w.Runes);
        Assert.Empty(j.Ledger.Entries);
        Assert.Null(w.Tickets[a]);
        Assert.Equal(SalvageBlock.Changed, outs[0].Reason);
    }

    [Fact]
    public void Without_a_ticket_nothing_is_taken()
    {
        var (w, j, outs) = Rig();
        w.TicketWorks = false;
        var a = w.Add(10, 9);
        j.Enqueue(Player, [a], outs.Add);
        Assert.True(w.Bag.ContainsKey(a));
        Assert.Empty(j.Ledger.Entries);
    }

    [Fact]
    public void Runes_that_do_not_fit_after_the_take_are_owed_and_paid_later()
    {
        var (w, j, outs) = Rig();
        w.GiveWorks = false;
        var a = w.Add(10, 9);
        j.Enqueue(Player, [a], outs.Add);
        Assert.Empty(w.Bag);
        Assert.Equal(9, j.OwedRunes(Player));
        Assert.Equal(LedgerState.Owed, Assert.Single(j.Ledger.Entries).State);
        Assert.Contains(w.Lines, l => l.Contains("owed"));
        w.GiveWorks = true;
        j.PayOwed(Player);
        Assert.Equal(9, w.Runes);
        Assert.Empty(j.Ledger.Entries);
    }

    [Fact]
    public void Each_state_change_is_saved_into_the_ledger()
    {
        var (w, j, _) = Rig();
        j.Enqueue(Player, [w.Add(10, 9)], null);
        Assert.Equal(3, w.Saves);   // taking, owed, paid
    }

    [Fact]
    public void A_step_that_throws_skips_the_item_and_the_queue_goes_on_with_the_runes_owed()
    {
        var (w, j, outs) = Rig();
        w.GiveThrows = true;
        j.Enqueue(Player, [w.Add(10, 9), w.Add(11, 5)], outs.Add);
        Assert.Single(outs);                                   // the request ended
        Assert.Empty(w.Bag);                                   // both taken
        Assert.Equal(14, j.OwedRunes(Player));                 // and both owed, paid by PayOwed
        w.GiveThrows = false;
        j.PayOwed(Player);
        Assert.Equal(14, w.Runes);
    }

    [Fact]
    public void A_player_whose_account_is_not_known_salvages_nothing()
    {
        var (w, j, outs) = Rig();
        w.Steam = 0;
        var a = w.Add(10, 9);
        j.Enqueue(Player, [a], outs.Add);
        Assert.True(w.Bag.ContainsKey(a));
        Assert.Empty(j.Ledger.Entries);
    }

    [Fact]
    public void An_item_named_twice_is_salvaged_once()
    {
        var (w, j, outs) = Rig();
        var a = w.Add(10, 9);
        j.Enqueue(Player, [a, a], outs.Add);
        Assert.Equal(9, w.Runes);
        Assert.Equal(1, outs[0].Salvaged);
    }
}
