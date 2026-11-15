// SPDX-License-Identifier: MIT
// The container visit memory: what to do with a target, harvest once, the first accept of a pickup.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class VisitTableTests
{
    private readonly FakeClock _clock = new();
    private readonly VisitTable _t;

    public VisitTableTests() => _t = new VisitTable(_clock);

    private TargetStep At(long ms, bool offer = true, uint id = 7, float delay = 1f, bool once = false, ulong host = 1)
    {
        _clock.NowMs = ms;
        return _t.Next(host, offer, id, delay, once);
    }

    [Fact]
    public void Waits_lootDelay_then_accepts_then_loots_when_the_offer_changes()
    {
        Assert.Equal(TargetStep.Wait, At(0));
        Assert.Equal(TargetStep.Wait, At(999));
        Assert.Equal(TargetStep.Accept, At(1000));
        _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Wait, At(1100));
        Assert.Equal(TargetStep.Loot, At(1200, id: 8));
    }

    [Fact]
    public void Delay_is_in_tenths_and_zero_means_at_once()
    {
        Assert.Equal(TargetStep.Wait, At(0, delay: 0.1f));
        Assert.Equal(TargetStep.Wait, At(99, delay: 0.1f));
        Assert.Equal(TargetStep.Accept, At(100, delay: 0.1f));
        Assert.Equal(TargetStep.Accept, At(0, delay: 0f, host: 2));
    }

    [Fact]
    public void No_usable_offer_loots_directly()
    {
        Assert.Equal(TargetStep.Wait, At(0, offer: false));
        Assert.Equal(TargetStep.Loot, At(1000, offer: false));
        Assert.Equal(TargetStep.Loot, At(1100, offer: false));   // not harvestOnce: again
    }

    [Fact]
    public void Refused_accept_is_retried_after_3000_ms_up_to_3_then_loot_anyway()
    {
        Assert.Equal(TargetStep.Wait, At(0));
        Assert.Equal(TargetStep.Accept, At(1000)); _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Wait, At(4000));                  // 3000 is not > 3000
        Assert.Equal(TargetStep.Accept, At(4001)); _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Wait, At(7001));
        Assert.Equal(TargetStep.Accept, At(7002)); _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Wait, At(10002));
        Assert.Equal(TargetStep.Loot, At(10003));                 // 3 accepts: loot anyway
    }

    [Fact]
    public void A_refused_accept_ten_seconds_later_in_the_same_visit_is_sent_again()
    {
        At(0);
        Assert.Equal(TargetStep.Accept, At(1000)); _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Accept, At(11000));               // 10000 unseen is not > 10000: same visit, retry due
    }

    [Fact]
    public void Re_arm_resets_every_field()
    {
        At(0, host: 5);
        Assert.Equal(TargetStep.Accept, At(1000, host: 5)); _t.Accepted(5, 7);
        Assert.Equal(TargetStep.Wait, At(2000, host: 5));
        Assert.Equal(TargetStep.Wait, At(12001, host: 5));        // 10001 unseen: new visit, openDelay again
        Assert.Equal(TargetStep.Accept, At(13001, host: 5));      // accepts were reset
    }

    [Fact]
    public void Exactly_10000_ms_unseen_is_the_same_visit()
    {
        At(0, host: 6);
        Assert.Equal(TargetStep.Accept, At(1000, host: 6)); _t.Accepted(6, 7);
        Assert.Equal(TargetStep.Loot, At(11000, host: 6, id: 9)); // same visit: the offer changed -> loot
    }

    [Fact]
    public void Plants_harvest_once_per_visit_after_the_accept()
    {
        At(0, once: true);
        Assert.Equal(TargetStep.Accept, At(1000, once: true)); _t.Accepted(1, 7);
        Assert.Equal(TargetStep.Loot, At(1100, id: 8, once: true)); _t.Looted(1);
        Assert.Equal(TargetStep.AlreadyHarvested, At(1200, id: 8, once: true));
        Assert.Equal(TargetStep.AlreadyHarvested, At(1300, offer: false, once: true));
    }

    [Fact]
    public void Plants_harvest_once_also_without_an_offer_and_after_refused_accepts()
    {
        At(0, offer: false, once: true);
        Assert.Equal(TargetStep.Loot, At(1000, offer: false, once: true));
        Assert.Equal(TargetStep.Loot, At(1100, offer: false, once: true));   // not looted yet (inject cap): still due
        _t.Looted(1);
        Assert.Equal(TargetStep.AlreadyHarvested, At(1200, offer: false, once: true));

        At(0, once: true, host: 2);
        foreach (var ms in new long[] { 1000, 4001, 7002 })
        {
            Assert.Equal(TargetStep.Accept, At(ms, once: true, host: 2));
            _t.Accepted(2, 7);
        }
        Assert.Equal(TargetStep.Loot, At(10003, once: true, host: 2)); _t.Looted(2);
        Assert.Equal(TargetStep.AlreadyHarvested, At(13004, once: true, host: 2));
        // a new visit harvests again
        Assert.Equal(TargetStep.Wait, At(23005, offer: false, once: true, host: 2));
        Assert.Equal(TargetStep.Loot, At(24005, offer: false, once: true, host: 2));
    }

    [Fact]
    public void Table_holds_1024_and_reuses_the_oldest()
    {
        _clock.NowMs = 0;
        Assert.Equal(TargetStep.Accept, _t.Next(0, true, 5, 0f, false));
        _t.Accepted(0, 5);
        for (ulong h = 1; h < 1024; h++) { _clock.NowMs = (long)h; _t.Next(h, true, 5, 0f, false); }
        Assert.Equal(1024, _t.Count);
        _clock.NowMs = 1024;
        Assert.Equal(TargetStep.Wait, _t.Next(0, true, 5, 0f, false));   // remembered: accepted 1024 ms ago
        _clock.NowMs = 1025;
        _t.Next(5000, true, 5, 0f, false);                                // full: host 1 (seen at 1) is the oldest now
        Assert.Equal(1024, _t.Count);
        _clock.NowMs = 1026;
        Assert.Equal(TargetStep.Wait, _t.Next(0, true, 5, 0f, false));    // host 0 kept (refreshed at 1024)
        _t.Accepted(2, 5);                                                // host 2 still there
        _clock.NowMs = 1027;
        Assert.Equal(TargetStep.Wait, _t.Next(2, true, 5, 0f, false));
        _clock.NowMs = 1028;
        Assert.Equal(TargetStep.Accept, _t.Next(1, true, 5, 0f, false));  // host 1 was forgotten: fresh (and evicts 3)
    }

    [Fact]
    public void FirstAcceptDue_once_per_visit()
    {
        _clock.NowMs = 0;
        Assert.True(_t.FirstAcceptDue(9));
        Assert.False(_t.FirstAcceptDue(9));
        _t.Accepted(9, 1);
        _clock.NowMs = 5000;
        Assert.False(_t.FirstAcceptDue(9));
        _clock.NowMs = 15001;
        Assert.True(_t.FirstAcceptDue(9));   // re-armed after 10001 ms unseen
        _clock.NowMs = 15002;
        Assert.False(_t.FirstAcceptDue(9));
    }
}
