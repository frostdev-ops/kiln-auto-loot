// SPDX-License-Identifier: MIT
// Pickups, inventory full, the "left behind" notices, enemy pause.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class PickupTrackerTests
{
    private readonly FakeClock _clock = new();

    private bool Take(PickupTracker p, long ms, bool wanted, out bool full, ulong key = 1)
    {
        _clock.NowMs = ms;
        return p.MayTake(key, wanted, out full);
    }

    [Fact]
    public void One_take_per_1500_ms()
    {
        var p = new PickupTracker(_clock);
        Assert.True(Take(p, 0, true, out _)); p.Taken(1);
        Assert.False(Take(p, 100, true, out _));
        Assert.False(Take(p, 900, true, out _));
        Assert.True(Take(p, 1500, true, out _));
    }

    [Fact]
    public void Stuck_after_3000_ms_then_10000_ms_back_off_then_a_fresh_try()
    {
        var p = new PickupTracker(_clock);
        bool full;
        for (long ms = 0; ms <= 3000; ms += 500) Assert.True(Take(p, ms, true, out full));   // 3000 is not > 3000
        Assert.False(Take(p, 3001, true, out full));
        Assert.True(full);                                       // "inventory full" once
        Assert.True(p.Blocked(1));
        for (long ms = 3500; ms < 13001; ms += 500) { Assert.False(Take(p, ms, true, out full)); Assert.False(full); }
        Assert.True(Take(p, 13001, true, out full));             // back off over: fresh try
        Assert.False(p.Blocked(1));
        for (long ms = 13500; ms <= 16001; ms += 500) Assert.True(Take(p, ms, true, out _));
        Assert.False(Take(p, 16002, true, out full));            // stuck again
        Assert.False(full);                                      // within 60 s of the last notice
    }

    [Fact]
    public void An_unseen_gap_over_1000_ms_starts_a_new_episode()
    {
        var p = new PickupTracker(_clock);
        foreach (var ms in new long[] { 0, 1001, 2002, 3003, 4004 }) Assert.True(Take(p, ms, true, out _));
        // the gap also ends a back off
        for (long ms = 5000; ms <= 8000; ms += 500) Take(p, ms, true, out _);
        Assert.False(Take(p, 8500, true, out _));
        Assert.True(Take(p, 9501, true, out _));
    }

    [Fact]
    public void Not_wanted_pushes_the_episode_start_and_never_raises_full()
    {
        var p = new PickupTracker(_clock);
        for (long ms = 0; ms <= 9000; ms += 500)
        {
            Assert.False(Take(p, ms, false, out var full));
            Assert.False(full);
        }
        for (long ms = 9500; ms <= 12000; ms += 500) Assert.True(Take(p, ms, true, out _));   // start was 9000
        Assert.False(Take(p, 12001, true, out var raised));
        Assert.True(raised);
    }

    [Fact]
    public void Recently_seen_within_250_ms()
    {
        var p = new PickupTracker(_clock);
        Assert.False(p.SeenRecently(1));
        Take(p, 1000, false, out _);
        _clock.NowMs = 1250;
        Assert.True(p.SeenRecently(1));
        _clock.NowMs = 1251;
        Assert.False(p.SeenRecently(1));
    }

    [Fact]
    public void Full_notice_gap_is_shared_with_containers()
    {
        var full = new FullTracker(_clock);
        var p = new PickupTracker(_clock, full);
        _clock.NowMs = 0;
        Assert.True(full.MayNotify());                           // a container notice at 0
        for (long ms = 0; ms <= 3000; ms += 500) Take(p, ms, true, out _);
        Assert.False(Take(p, 3001, true, out var raised));
        Assert.False(raised);                                    // within 60 s of the container notice
        _clock.NowMs = 70000;
        Assert.True(full.MayNotify());
    }

    [Fact]
    public void Each_pickup_has_its_own_state()
    {
        var p = new PickupTracker(_clock);
        Assert.True(Take(p, 0, true, out _, key: 1)); p.Taken(1);
        Assert.True(Take(p, 10, true, out _, key: 2));
    }
}

public class FullTrackerTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void More_than_100_attempts_on_one_id_within_5000_ms()
    {
        var f = new FullTracker(_clock);
        for (int i = 0; i < 100; i++) Assert.False(f.ContainerAttempt(42));
        Assert.True(f.ContainerAttempt(42));                     // the 101st
        Assert.False(f.ContainerAttempt(42));                    // one notice per 60 s
    }

    [Fact]
    public void Window_restarts_on_another_id_or_after_5000_ms()
    {
        var f = new FullTracker(_clock);
        for (int i = 0; i < 100; i++) f.ContainerAttempt(1);
        Assert.False(f.ContainerAttempt(2));                     // another id: count 1
        for (int i = 0; i < 99; i++) f.ContainerAttempt(2);
        _clock.NowMs = 5001;
        Assert.False(f.ContainerAttempt(2));                     // window over: count 1
        _clock.NowMs = 10001;                                    // 5000 after the window start: same window
        for (int i = 0; i < 99; i++) Assert.False(f.ContainerAttempt(2));
        Assert.True(f.ContainerAttempt(2));                      // the 101st of the window that started at 5001
        _clock.NowMs = 100_000;
        for (int i = 0; i < 100; i++) Assert.False(f.ContainerAttempt(3));
    }

    [Fact]
    public void Notices_60000_ms_apart()
    {
        var f = new FullTracker(_clock);
        _clock.NowMs = 0;
        Assert.True(f.MayNotify());
        _clock.NowMs = 60000;
        Assert.False(f.MayNotify());
        _clock.NowMs = 60001;
        Assert.True(f.MayNotify());
    }
}

public class NoticeBookTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void Three_per_item_per_session_and_10_s_apart()
    {
        var b = new NoticeBook(_clock);
        Assert.True(b.ShouldNotify(7, true));
        _clock.NowMs = 9999;
        Assert.False(b.ShouldNotify(7, true));
        Assert.True(b.ShouldNotify(8, true));                    // another item is independent
        _clock.NowMs = 10000;
        Assert.True(b.ShouldNotify(7, true));
        _clock.NowMs = 20000;
        Assert.True(b.ShouldNotify(7, true));
        _clock.NowMs = 1_000_000;
        Assert.False(b.ShouldNotify(7, true));                   // three shown
        Assert.Equal(3, b.Shown(7));
    }

    [Fact]
    public void Switch_off_shows_nothing_and_counts_nothing()
    {
        var b = new NoticeBook(_clock);
        Assert.False(b.ShouldNotify(7, false));
        Assert.Equal(0, b.Shown(7));
        Assert.True(b.ShouldNotify(7, true));
    }
}

public class EnemyPauseTests
{
    [Fact]
    public void Paused_until_2000_ms_after_the_last_near_enemy()
    {
        var clock = new FakeClock();
        var e = new EnemyPause(clock);
        Assert.False(e.Paused(true));
        clock.NowMs = 500;
        e.EnemySeen();
        Assert.True(e.Paused(true));
        Assert.False(e.Paused(false));                           // enemyPause off
        clock.NowMs = 2499;
        Assert.True(e.Paused(true));
        clock.NowMs = 2500;
        Assert.False(e.Paused(true));
    }
}
