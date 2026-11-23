// SPDX-License-Identifier: MIT
// The scan's step budget (HadToRegister 2026-11-20: the first scan after a fast travel held the server 5.3 s): a burst
// of targets new to the mod is looked at over several server steps, no step spends more than the budget (plus the one
// call that crossed it), and a step that is slow anyway leaves one "slow scan" line in kiln.log per minute.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class ScanBudgetTests(Xunit.ITestOutputHelper output)
{
    private static readonly string[] None = [];

    /// <summary>Where the player arrives (a fast travel 1 km away).</summary>
    private const double There = 1000;

    /// <summary>A player who just arrived somewhere: <paramref name="n"/> entities the mod has never looked at, all in
    /// reach, each Spawn.Inspect costing <paramref name="costMicros"/>. They were far away during earlier scans,
    /// and the next step's scan is the first to have them in reach.</summary>
    private static (LoopRig Rig, List<FakeEntity> Burst) Arrive(int n, long costMicros)
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var burst = new List<FakeEntity>();
        for (var i = 0; i < n; i++) burst.Add(rig.Game.Add(LoopRig.Unknown, There + (i % 140) / 10.0, None, pickupItem: 1));
        for (var i = 0; i < 6; i++) rig.StepAfter(200);   // scans with nothing in reach: every list found
        rig.Game.InspectCostMicros = costMicros;
        rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { Position = new WorldPosition(There, 0, 0) };
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
        var twig = rig.Game.Add(LoopRig.Twig, There + 0.5, None, pickupItem: 1);   // nearest of all (new: found in the next scan)
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
        rig.Game.Add(LoopRig.Twig, There + 13.9, None, pickupItem: 1);   // far end of the burst
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
        var again = rig.Game.Add(LoopRig.Unknown, There + 2, None, pickupItem: 1);
        rig.Game.InspectCost[again.Id] = 2_000_000;
        for (var i = 0; i < 10; i++) rig.StepAfter(200);
        Assert.Single(rig.KeyLines, l => l.StartsWith("slow scan:", StringComparison.Ordinal));

        // a minute after the first: the next slow step is named
        rig.Clock.NowMs += LootLoop.SlowLineGapMs;
        var third = rig.Game.Add(LoopRig.Unknown, There + 3, None, pickupItem: 1);
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

    // ---- 0.2.8, the owner's hosted game on Kiln 30.8: "slow scan: 56-113 ms in one server step, most in the scan's
    // start (Players, World.Find, Nearby.Within). start 89 ms: 1 player(s), 4 lists of 10252 entities". Every 100 ms
    // the start found four lists over the whole world and read the position of every candidate in them, though only the
    // few within reach of a player matter. A world of 12,000 candidates, all but a few far away:

    private static readonly string[][] Lists =
        [[LootLoop.PickupComponent], [LootLoop.OfferComponent], [LootLoop.InventoryComponent], [LootLoop.LootMarkerComponent]];

    private static LoopRig BigWorld(int n = 12_000, bool hasMod = true)
    {
        var rig = new LoopRig(s => s.OpenDelay = 0, hasMod: hasMod);
        rig.Game.FindCostMicros = 600;     // one pass over the world's entities
        rig.Game.PositionCostMicros = 2;   // one position read
        for (var i = 0; i < n; i++) rig.Game.Add(LoopRig.Unknown, 100 + (i * 7919L) % 4000, Lists[i % 4]);
        return rig;
    }

    [Fact]
    public void A_world_of_12000_candidates_keeps_every_step_of_the_scan_within_the_budget()
    {
        var rig = BigWorld();
        var twig = rig.Game.Add(LoopRig.Twig, 2, None, pickupItem: 1);
        long worst = 0;
        for (var i = 0; i < 125; i++) worst = Math.Max(worst, rig.StepAfter());   // two seconds of 16 ms steps
        output.WriteLine($"12k far candidates: worst step {worst} us; {rig.Loop.Counters.ScanCount} complete scans; {rig.Game.PositionReads} position reads");
        Assert.True(worst <= LootLoop.StepBudgetMicros + 600, $"a step spent {worst} us");
        Assert.Contains(rig.Game.Loots, l => l.Host == twig.Id);
        // Full fresh positions, not a speed-bound heuristic: every completed scan rechecks the far candidates.
        Assert.True(rig.Game.PositionReads >= 12_000, $"{rig.Game.PositionReads} position reads");
    }

    [Fact]
    public void After_a_fast_travel_into_the_far_part_of_the_world_what_is_in_reach_there_is_taken()
    {
        var rig = BigWorld();
        var there = rig.Game.Add(LoopRig.Twig, 3001, None, pickupItem: 1);
        for (var i = 0; i < 60; i++) rig.StepAfter();
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == there.Id);
        rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { Position = new WorldPosition(3000, 0, 0) };
        for (var i = 0; i < 125 && !rig.Game.Loots.Any(l => l.Host == there.Id); i++) rig.StepAfter();
        Assert.Contains(rig.Game.Loots, l => l.Host == there.Id);
    }

    [Fact]
    public void Something_new_next_to_the_player_or_coming_close_is_taken_soon()
    {
        var rig = BigWorld();
        var coming = rig.Game.Add(LoopRig.Twig, 60, None, pickupItem: 1);   // beyond reach
        for (var i = 0; i < 60; i++) rig.StepAfter();
        var fresh = rig.Game.Add(LoopRig.Twig, 1, None, pickupItem: 1);     // dropped right next to the player
        coming.Position = new WorldPosition(4, 0, 0);                        // it came close
        var steps = 0;
        while (steps < 200 && !(rig.Game.Loots.Any(l => l.Host == fresh.Id) && rig.Game.Loots.Any(l => l.Host == coming.Id)))
        {
            rig.StepAfter();
            steps++;
        }
        Assert.Contains(rig.Game.Loots, l => l.Host == fresh.Id);
        Assert.Contains(rig.Game.Loots, l => l.Host == coming.Id);
        Assert.True(steps * 16 <= 2500, $"{steps * 16} ms");
    }
    [Fact]
    public void A_warm_world_of_12000_in_range_candidates_slices_position_reads_too()
    {
        var rig = BigWorld();
        foreach (var e in rig.Game.Entities.Values) e.Position = new WorldPosition(2, 0, 0);
        // Warm every list before measuring: the interrupted cache exempted known-near positions from its budget.
        rig.Loop.Scan();
        long worst = 0;
        for (var i = 0; i < 200; i++) worst = Math.Max(worst, rig.StepAfter());
        output.WriteLine($"12k near candidates: worst step {worst} us; {rig.Loop.Counters.ScanCount} complete scans");
        Assert.True(worst <= LootLoop.StepBudgetMicros + 600, $"a step spent {worst} us");
    }

    [Fact]
    public void New_corpses_in_any_candidate_list_are_seen_by_the_next_complete_scan()
    {
        var rig = BigWorld();
        rig.Loop.Scan();
        var scans = rig.Loop.Counters.ScanCount;
        var corpse = rig.Game.Add(LoopRig.Vukah, 2, [LootLoop.InventoryComponent]);
        corpse.Slots = [new ContainerSlot(1, 7, 1)];
        for (var i = 0; i < 500 && rig.Loop.Counters.ScanCount == scans; i++) rig.StepAfter();
        Assert.True(rig.Loop.Counters.ScanCount > scans);
        Assert.Contains(corpse.Id, rig.Game.Inspected);
    }

    [Fact]
    public void Nearest_targets_go_first_even_when_their_component_list_comes_last()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var far = rig.Game.Add(LoopRig.Unknown, 12, [LootLoop.PickupComponent]);
        var near = rig.Game.Add(LoopRig.Unknown, 1, [LootLoop.LootMarkerComponent]);
        rig.Game.InspectCostMicros = LootLoop.StepBudgetMicros;
        rig.StepAfter();
        Assert.Contains(near.Id, rig.Game.Inspected);
        Assert.DoesNotContain(far.Id, rig.Game.Inspected);
    }

    [Fact]
    public void A_slow_indivisible_Find_is_reported_not_misrepresented_as_a_hard_4ms_bound()
    {
        var rig = BigWorld();
        rig.Game.FindCostMicros = 80_000;
        var spent = rig.StepAfter();
        Assert.Equal(80_000, spent);  // no position reads or target is forced after this call
        Assert.Equal(0, rig.Game.PositionReads);
        var line = Assert.Single(rig.KeyLines, l => l.StartsWith("slow scan:"));
        Assert.Contains("1 World.Find (80 ms, slowest call 80 ms)", line);
        Assert.Contains("0 positions read", line);
        output.WriteLine(line);
    }

    [Fact]
    public void Players_and_individual_position_reads_have_separate_slow_call_evidence()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        rig.Game.PlayerCostMicros = 55_000;
        rig.StepAfter();
        Assert.Contains("Players 55 ms", Assert.Single(rig.KeyLines));
        rig.Game.PlayerCostMicros = 0;
        rig.Clock.NowMs += LootLoop.SlowLineGapMs;
        rig.Game.Add(LoopRig.Twig, 1, [], pickupItem: 1);
        rig.Game.PositionCostMicros = 60_000;
        for (var i = 0; i < 10 && rig.KeyLines.Count < 2; i++) rig.StepAfter();
        Assert.Contains("1 positions read (60 ms, slowest call 60 ms)", rig.KeyLines[1]);
        Assert.Equal(2, rig.KeyLines.Count);
    }

    [Fact]
    public void Shared_component_entries_read_their_position_once_per_scan_and_work_nearest_first()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        for (var i = 0; i < 12_000; i++) rig.Game.Add(LoopRig.Unknown, 100, Lists.SelectMany(x => x).ToArray());
        var twig = rig.Game.Add(LoopRig.Twig, 2, [], pickupItem: 1);
        rig.Game.PositionCostMicros = 2;
        var scans = rig.Loop.Counters.ScanCount;
        long worst = 0;
        while (rig.Loop.Counters.ScanCount == scans) worst = Math.Max(worst, rig.StepAfter());
        Assert.InRange(rig.Game.PositionReads, 12_001, 12_002);  // shared discovery plus the live target read
        Assert.Contains(rig.Game.Loots, l => l.Host == twig.Id);
        Assert.True(worst <= LootLoop.StepBudgetMicros + 2, $"a step spent {worst} us");
    }

    [Fact]
    public void Enemy_classification_during_preparation_is_also_time_sliced()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.EnemyPause = true; });
        for (var i = 0; i < 100; i++) rig.Game.Add(LoopRig.Unknown, 2, [LootLoop.EnemyComponent]);
        rig.Game.InspectCostMicros = 1000;
        var scans = rig.Loop.Counters.ScanCount;
        long worst = 0;
        while (rig.Loop.Counters.ScanCount == scans) worst = Math.Max(worst, rig.StepAfter());
        Assert.Equal(100, rig.Game.InspectCalls);
        Assert.True(worst <= LootLoop.StepBudgetMicros + 1000, $"a step spent {worst} us");
    }

    [Fact]
    public void An_opened_game_window_during_preparation_prevents_prepared_loot()
    {
        var rig = BigWorld(hasMod: false);
        var twig = rig.Game.Add(LoopRig.Twig, 2, [], pickupItem: 1);
        rig.StepAfter();
        rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { UiOpen = true };
        var scans = rig.Loop.Counters.ScanCount;
        while (rig.Loop.Counters.ScanCount == scans) rig.StepAfter();
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == twig.Id);
    }

}
