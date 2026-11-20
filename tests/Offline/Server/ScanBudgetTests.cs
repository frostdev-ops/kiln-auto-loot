// SPDX-License-Identifier: MIT
// The scan's step budget (HadToRegister 2026-11-20: the first scan after a fast travel held the server 5.3 s): a burst
// of targets new to the mod is looked at over several server steps, no step spends more than the budget (plus the one
// call that crossed it), and a step that is slow anyway leaves one "slow scan" line in kiln.log per minute.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class ScanBudgetTests
{
    private static readonly string[] None = [];

    /// <summary>A player who just arrived somewhere: <paramref name="n"/> entities the mod has never looked at, all in
    /// reach, each Spawn.Inspect costing <paramref name="costMicros"/>.</summary>
    private static (LoopRig Rig, List<FakeEntity> Burst) Arrive(int n, long costMicros)
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        rig.StepAfter(200);   // a scan with nothing around
        rig.Game.InspectCostMicros = costMicros;
        var burst = new List<FakeEntity>();
        for (var i = 0; i < n; i++) burst.Add(rig.Game.Add(LoopRig.Unknown, (i % 140) / 10.0, None, pickupItem: 1));
        return (rig, burst);
    }

    [Fact]
    public void A_burst_of_3000_new_targets_after_a_fast_travel_is_spread_over_steps_within_the_budget()
    {
        const long cost = 500;   // 1.5 s of work in all
        var (rig, burst) = Arrive(3000, cost);
        var steps = 0;
        long worst = 0;
        while (rig.Game.Inspected.Count < burst.Count && steps < 5000)
        {
            worst = Math.Max(worst, rig.StepAfter());
            steps++;
        }
        Assert.All(burst, e => Assert.Contains(e.Id, rig.Game.Inspected));   // every one was looked at in the end
        Assert.True(worst <= LootLoop.StepBudgetMicros + cost, $"a step spent {worst} us");
        Assert.True(steps >= 3000 * cost / (LootLoop.StepBudgetMicros + cost), $"only {steps} steps");
    }

    [Fact]
    public void Targets_near_the_player_are_still_taken_while_a_burst_is_worked_through()
    {
        var (rig, _) = Arrive(2000, 500);
        var twig = rig.Game.Add(LoopRig.Twig, 0.5, None, pickupItem: 1);   // nearest of all
        for (var i = 0; i < 400 && rig.Game.Loots.Count == 0; i++) rig.StepAfter();
        Assert.Contains(rig.Game.Loots, l => l.Host == twig.Id);
    }

    [Fact]
    public void No_new_scan_starts_before_the_one_in_progress_is_finished()
    {
        var (rig, burst) = Arrive(1000, 1000);
        var scans = rig.Loop.Counters.Scans;
        rig.StepAfter(200);                       // starts the scan of the burst
        Assert.Equal(scans + 1, rig.Loop.Counters.Scans);
        for (var i = 0; i < 20; i++) rig.StepAfter(200);   // 4 s later it is still going: no second scan
        Assert.True(rig.Game.Inspected.Count < burst.Count);
        Assert.Equal(scans + 1, rig.Loop.Counters.Scans);
        while (rig.Game.Inspected.Count < burst.Count) rig.StepAfter(16);
        rig.StepAfter(200);
        Assert.True(rig.Loop.Counters.Scans > scans + 1);   // finished: the next scan comes
    }

    [Fact]
    public void A_player_who_leaves_while_their_scan_is_spread_out_is_dropped_from_it()
    {
        var (rig, _) = Arrive(1000, 1000);
        rig.Game.Add(LoopRig.Twig, 13.9, None, pickupItem: 1);   // far end of the burst
        rig.StepAfter(200);
        rig.Loop.Leave(rig.Player);
        rig.Game.PlayerList.Clear();
        var calls = rig.Game.InspectCalls;
        for (var i = 0; i < 300; i++) rig.StepAfter();
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(calls, rig.Game.InspectCalls);   // the rest of their targets was not looked at
    }

    [Fact]
    public void A_slow_step_writes_one_slow_scan_line_a_minute_naming_the_phase_and_counts()
    {
        var (rig, burst) = Arrive(3, 10);
        rig.Game.InspectCost[burst[0].Id] = 5_000_000;   // one call holds the step 5 s (a cold page, a template re-read)
        rig.StepAfter(200);
        var slow = rig.KeyLines.Where(l => l.StartsWith("slow scan:", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(slow);
        Assert.Contains("5000 ms", line);
        Assert.Contains("Spawn.Inspect", line);   // the phase
        Assert.Contains("slowest call 5000 ms", line);
        Assert.Contains("1 new", line);

        // another slow step within the minute: no second line
        var again = rig.Game.Add(LoopRig.Unknown, 2, None, pickupItem: 1);
        rig.Game.InspectCost[again.Id] = 2_000_000;
        for (var i = 0; i < 10; i++) rig.StepAfter(200);
        Assert.Single(rig.KeyLines, l => l.StartsWith("slow scan:", StringComparison.Ordinal));

        // a minute after the first: the next slow step is named
        rig.Clock.NowMs += LootLoop.SlowLineGapMs;
        var third = rig.Game.Add(LoopRig.Unknown, 3, None, pickupItem: 1);
        rig.Game.InspectCost[third.Id] = 2_000_000;
        for (var i = 0; i < 10; i++) rig.StepAfter(200);
        Assert.Equal(2, rig.KeyLines.Count(l => l.StartsWith("slow scan:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Steps_within_the_budget_write_no_slow_line()
    {
        var (rig, _) = Arrive(500, 100);
        for (var i = 0; i < 200; i++) rig.StepAfter();
        Assert.DoesNotContain(rig.KeyLines, l => l.StartsWith("slow scan:", StringComparison.Ordinal));
    }
}
