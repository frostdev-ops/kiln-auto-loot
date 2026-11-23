// SPDX-License-Identifier: MIT
// 0.2.8, the owner's decision: the height window was a stand-in until Kiln could check line of sight. Where the server
// can (queries.line_of_sight, and it answers), what you can see decides what is in reach: "Only what I can see" is on
// by default and the height window is off (0 = no limit). The height window stays as the fallback where line of sight
// cannot be checked (or "Only what I can see" is off). Critters keep ignoring the height window (0.2.5). The checks
// stay inside the scan's step budget: one per target per second, nearest first.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class SightFirstTests
{
    private static readonly string[] Pickup = [];
    private static readonly string[] Swarm = [LootLoop.PickupComponent, LootLoop.OfferComponent];

    [Fact]
    public void The_defaults_let_line_of_sight_decide_and_switch_the_height_window_off()
    {
        var s = new PersonalSettings();
        Assert.True(s.VisibleOnly);
        Assert.Equal(0f, s.HeightAbove);
        Assert.Equal(0f, s.HeightBelow);
    }

    private static (LoopRig Rig, FakeEntity Corpse) CorpseBelow(double dy, Action<PersonalSettings>? more = null)
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.HeightBelow = 4; s.HeightAbove = 4; more?.Invoke(s); });
        var corpse = rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv, y: dy);
        corpse.Offer = Offer.None;                 // no offer: looted at once
        corpse.Slots = [new ContainerSlot(1, 2, 3)];
        return (rig, corpse);
    }

    [Fact]
    public void Where_line_of_sight_decides_a_corpse_down_a_slope_in_plain_view_is_looted_whatever_the_height_window()
    {
        var (rig, _) = CorpseBelow(-6);           // HadToRegister's Vukah corpses: down a slope, 6 m below
        rig.ScanFor(1000);
        Assert.NotEmpty(rig.Game.Loots);
    }

    [Fact]
    public void Where_line_of_sight_decides_a_corpse_out_of_sight_is_left()
    {
        var (rig, corpse) = CorpseBelow(-1);
        corpse.Sight = Sight.Hidden;
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void Without_line_of_sight_the_height_window_is_the_fallback()
    {
        var (rig, corpse) = CorpseBelow(-6);
        rig.Game.SightOn = false;                  // this game or server cannot check line of sight
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Loots);
        corpse.Position = new WorldPosition(3, -3, 0);
        rig.ScanFor(1000);
        Assert.NotEmpty(rig.Game.Loots);
    }

    [Fact]
    public void While_line_of_sight_does_not_answer_the_height_window_is_the_fallback()
    {
        var (rig, corpse) = CorpseBelow(-6);
        var near = rig.Game.Add(LoopRig.Twig, 1, Pickup, pickupItem: 1);
        near.Sight = Sight.Unknown;                // the service is there but not loaded yet ("NotLoaded")
        rig.ScanFor(1000);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == corpse.Id);
        near.Sight = Sight.Visible;                // it answers again
        rig.ScanFor(2500);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
    }

    [Fact]
    public void With_Only_what_I_can_see_off_the_height_window_applies()
    {
        var (rig, _) = CorpseBelow(-6, s => s.VisibleOnly = false);
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void Critters_ignore_the_height_window_with_or_without_line_of_sight()
    {
        foreach (var sight in new[] { true, false })
        {
            var rig = new LoopRig(s => { s.OpenDelay = 0; s.HeightAbove = 4; s.VisibleOnly = true; });
            rig.Game.SightOn = sight;
            rig.Game.Add(LoopRig.Frogs, 2, Swarm, y: 8);   // a swarm 8 m up, within the main radius in 3D
            rig.ScanFor(1000);
            Assert.NotEmpty(rig.Game.Loots);
        }
    }

    [Fact]
    public void Line_of_sight_checks_stay_inside_the_step_budget_one_per_target_a_second_nearest_first()
    {
        const long cost = 400;
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var twigs = new List<FakeEntity>();
        for (var i = 0; i < 300; i++) twigs.Add(rig.Game.Add(LoopRig.Twig, 1000.5 + i * 0.04, Pickup, pickupItem: 1));
        for (var i = 0; i < 6; i++) rig.StepAfter(200);                   // far away: every list found, nothing in reach
        rig.Game.SightCostMicros = cost;
        rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { Position = new WorldPosition(1000, 0, 0) };   // arrived
        long worst = rig.StepAfter(100);                                  // the scan starts: nearest first
        Assert.Contains(rig.Game.Loots, l => l.Host == twigs[0].Id);
        for (var i = 0; i < 60; i++) worst = Math.Max(worst, rig.StepAfter());   // about a second of 16 ms steps
        Assert.True(worst <= LootLoop.StepBudgetMicros + cost, $"a step spent {worst} us");
        // about one second of steps: each twig asked at most once (cached for SightCacheMs), not once per scan
        Assert.True(rig.Game.SightCalls <= twigs.Count, $"{rig.Game.SightCalls} sight calls for {twigs.Count} targets");
    }

    // kiln.log's one line a world when line of sight gives no answer: "not available" only where the server has no
    // check; a check that did not answer (NotLoaded while a world loads) says so, and both say the height window decides
    [Fact]
    public void The_kiln_log_line_tells_no_line_of_sight_from_a_check_that_did_not_answer()
    {
        var missing = KilnGame.SightUnknownLine(null, "queries.line_of_sight is off");
        Assert.Contains("not available here (queries.line_of_sight is off)", missing);
        Assert.Contains("height window", missing);
        var notLoaded = KilnGame.SightUnknownLine("NotLoaded", "");
        Assert.Contains("did not answer (NotLoaded)", notLoaded);
        Assert.DoesNotContain("not available", notLoaded);
        Assert.Contains("height window", notLoaded);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_first_target_whose_sight_answer_is_unknown_already_uses_the_height_fallback(bool corpseTarget)
    {
        var (rig, corpse) = CorpseBelow(-6);
        FakeEntity target = corpse;
        if (!corpseTarget)
        {
            rig.Game.Remove(corpse);
            target = rig.Game.Add(LoopRig.Twig, 2, [], pickupItem: 1, y: -6);
        }
        target.Sight = Sight.Unknown;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Empty(rig.Game.Moves);
        Assert.True(rig.Loop.Counters.HeightOut > 0);
    }

}
