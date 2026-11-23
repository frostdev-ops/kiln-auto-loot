// SPDX-License-Identifier: MIT
// Only a real clear lower ray can rescue a body corpse whose standing-height ray hit a voxel.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class CorpseSightTests
{
    private static (LoopRig Rig, FakeEntity Corpse) Corpse(bool enemy = true)
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var corpse = rig.Game.Add(enemy ? LoopRig.Enemy : LoopRig.Vukah, 13.7, [LootLoop.InventoryComponent]);
        corpse.Dead = true;
        corpse.Slots = [new ContainerSlot(11, 7, 5)];
        corpse.Sight = Sight.Hidden;
        return (rig, corpse);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_real_clear_lower_ray_can_see_a_body_corpse_after_the_standing_sample_hit_a_voxel(bool enemy)
    {
        var (rig, corpse) = Corpse(enemy);
        corpse.BodySight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
    }

    [Fact]
    public void All_three_blocked_samples_leave_the_corpse_and_cache_the_complete_hidden_answer()
    {
        var (rig, _) = Corpse();
        rig.ScanFor(600);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Equal(1, rig.Loop.Counters.SightBlocked);
        Assert.Equal(1, rig.Game.UpperSightCalls);
        Assert.Equal(3, rig.Game.ActualSightRays);
    }

    [Fact]
    public void An_original_clear_ray_needs_no_body_sample()
    {
        var (rig, corpse) = Corpse();
        corpse.Sight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_deferred_or_unknown_lower_sample_never_overrides_the_known_voxel_or_caches_hidden(bool budgetExceeded)
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = budgetExceeded ? Sight.Later : Sight.Unknown;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
        Assert.DoesNotContain(rig.KeyLines, l => l.Contains("not in plain view"));
        corpse.BodySight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Game.PrimarySightCalls); // resume the lower sample, not another primary ray
        Assert.Equal(2, rig.Game.BodySightCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Living_enemies_chests_and_ground_props_get_no_corpse_geometry_override(int kind)
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var e = rig.Game.Add(kind == 0 ? LoopRig.Enemy : kind == 1 ? LoopRig.Chest : LoopRig.Twig, 3,
            [LootLoop.InventoryComponent], pickupItem: kind == 2 ? 1u : null);
        e.Sight = Sight.Hidden;
        e.BodySight = Sight.Visible;
        e.Slots = [new ContainerSlot(11, 7, 5)];
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Empty(rig.Game.Moves);
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
    }

    [Fact]
    public void Turning_Only_what_I_can_see_off_still_bypasses_both_rays()
    {
        var (rig, corpse) = Corpse();
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { OpenDelay = 0, VisibleOnly = false });
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(0, rig.Game.SightCalls);
    }

    [Fact]
    public void Near_reports_the_exact_blocked_sample_and_voxel_even_for_a_cached_decision()
    {
        var (rig, corpse) = Corpse();
        corpse.BodyObstruction = new SightHit(new WorldPosition(2, 0.1, 0), 2, 24, 0x35);
        corpse.UpperObstruction = new SightHit(new WorldPosition(3, 0.2, 0), 3, 25, 0x36);
        rig.ScanAfter(100); // the next reported scan uses the cached decision
        List<string>? log = null;
        rig.Loop.Explain(NearReport.Around(rig.Game, rig.Player, new WorldPosition(0, 0, 0), 15, [],
            (l, _) => log = l.ToList()));
        rig.ScanAfter(100);
        var line = Assert.Single(log!, l => l.StartsWith($"near: {corpse.Id} "));
        Assert.Contains("sight: primary", line);
        Assert.Contains("from (0, 1.5, 0) to (13.7, 1.5, 0)", line);
        Assert.Contains("voxel at (1, 1, 0), distance 1 m, material 23, voxel 0x0034", line);
        Assert.Contains("sight: corpse body", line);
        Assert.Contains("to (13.7, 0.35, 0): blocked by voxel at (2, 0.1, 0), distance 2 m, material 24, voxel 0x0035", line);
        Assert.Contains("sight: corpse body upper", line);
        Assert.Contains("to (13.7, 0.65, 0): blocked by voxel at (3, 0.2, 0), distance 3 m, material 25, voxel 0x0036", line);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Equal(1, rig.Game.UpperSightCalls);
    }
    [Fact]
    public void A_shared_one_ray_budget_resumes_only_the_lower_ray_in_a_later_step()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Visible;
        rig.Game.RaysLeft = 1;
        rig.StepAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(1, rig.Game.ActualSightRays);
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
        rig.Game.RaysLeft = 1;
        rig.StepAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(2, rig.Game.ActualSightRays);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(2, rig.Game.BodySightCalls);  // one BudgetExceeded query, then one actual ray
    }

    [Fact]
    public void A_primary_that_spends_the_step_time_budget_defers_the_lower_sample()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Visible;
        rig.Game.SightCostMicros = LootLoop.StepBudgetMicros;
        Assert.Equal(LootLoop.StepBudgetMicros, rig.StepAfter(100));
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(LootLoop.StepBudgetMicros, rig.StepAfter(100));
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pending_primary_is_rechecked_if_either_origin_moves(bool playerMoves)
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Later;
        rig.ScanAfter(100);
        if (playerMoves) rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { Position = new WorldPosition(1, 0, 0) };
        else corpse.Position = new WorldPosition(13.6, 0, 0);
        corpse.Sight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Equal(2, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
    }

    [Fact]
    public void Pending_work_expires_and_is_not_shared_with_a_recycled_entity_id()
    {
        var (rig, corpse) = Corpse();
        corpse.Serial = 0;
        corpse.BodySight = Sight.Unknown;
        rig.ScanAfter(100);
        var next = rig.Game.Reuse(corpse.Id, LoopRig.Enemy, 13.7, [LootLoop.InventoryComponent]);
        next.Serial = 0;
        next.Dead = true;
        next.Slots = [new ContainerSlot(11, 7, 5)];
        rig.ScanAfter(100);
        Assert.Equal(2, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Contains(rig.Game.Loots, l => l.Host == next.Id);

        var (expire, still) = Corpse();
        still.BodySight = Sight.Unknown;
        expire.ScanAfter(100);
        still.Sight = Sight.Visible;
        expire.ScanAfter(LootLoop.SightCacheMs);
        Assert.Equal(2, expire.Game.PrimarySightCalls);
        Assert.Equal(1, expire.Game.BodySightCalls);
        Assert.Contains(expire.Game.Loots, l => l.Host == still.Id);
    }

    [Fact]
    public void Hidden_without_a_real_voxel_hit_does_not_enable_a_lower_sample()
    {
        var (rig, corpse) = Corpse();
        corpse.SightObstruction = null;
        corpse.BodySight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
    }

    [Fact]
    public void A_creature_drop_is_not_a_body_corpse()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var pile = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.InventoryComponent]);
        pile.Slots = [new ContainerSlot(11, 7, 5)];
        pile.Sight = Sight.Hidden;
        pile.BodySight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
    }

    [Fact]
    public void An_unknown_primary_still_uses_the_existing_height_fallback_without_a_body_ray()
    {
        var (rig, corpse) = Corpse();
        corpse.Sight = Sight.Unknown;
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(0, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
    }

    [Fact]
    public void The_provider_options_change_only_the_target_offset_and_keep_obstruction_details()
    {
        var options = KilnGame.CorpseSightOptions;
        Assert.Equal(new WorldPosition(0, 1.5, 0), options.FromOffset);
        Assert.Equal(new WorldPosition(0, 0.35, 0), options.ToOffset);
        Assert.Equal(0, options.Realm);
        Assert.Equal(new WorldPosition(0, 1.5, 0), KilnGame.CorpseUpperSightOptions.FromOffset);
        Assert.Equal(new WorldPosition(0, 0.65, 0), KilnGame.CorpseUpperSightOptions.ToOffset);
        Assert.Equal(0, KilnGame.CorpseUpperSightOptions.Realm);
        var hit = new VoxelRayHit(new WorldPosition(1, 1, 0), 1, 23, 0x34);
        var blocked = KilnGame.ReadSight(new VoxelSightResult(true, null, false, hit));
        Assert.Equal(Sight.Hidden, blocked.State);
        Assert.Equal(new SightHit(hit.Position, hit.Distance, hit.Material, hit.Voxel), blocked.Obstruction);
        Assert.Equal(Sight.Visible, KilnGame.ReadSight(new VoxelSightResult(true, null, true, null)).State);
        Assert.Equal(Sight.Later, KilnGame.ReadSight(new VoxelSightResult(false, "BudgetExceeded", false, null)).State);
        Assert.Equal(Sight.Unknown, KilnGame.ReadSight(new VoxelSightResult(false, "NotLoaded", false, null)).State);
    }

    [Fact]
    public void Three_samples_per_corpse_still_respect_the_scan_step_time_budget()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var corpses = new List<FakeEntity>();
        for (var i = 0; i < 300; i++)
        {
            var e = rig.Game.Add(LoopRig.Enemy, 0.5 + i * 0.04, [LootLoop.InventoryComponent]);
            e.Dead = true;
            e.Slots = [new ContainerSlot(11, 7, 5)];
            e.Sight = Sight.Hidden;
            e.UpperSight = Sight.Visible;
            corpses.Add(e);
        }
        rig.Game.SightCostMicros = 400;
        long worst = 0;
        for (var i = 0; i < 1000 && corpses.Any(e => !rig.Game.Loots.Any(l => l.Host == e.Id)); i++)
        {
            worst = Math.Max(worst, rig.StepAfter());
            foreach (var e in corpses.Where(e => rig.Game.Loots.Any(l => l.Host == e.Id))) e.Slots = [];
        }
        Assert.All(corpses, e => Assert.Contains(rig.Game.Loots, l => l.Host == e.Id));
        Assert.True(worst <= LootLoop.StepBudgetMicros + 400, $"a step spent {worst} us");
    }

    [Fact]
    public void A_second_body_column_sample_may_be_used_only_after_both_previous_rays_hit_voxels()
    {
        var (rig, corpse) = Corpse();
        corpse.UpperSight = Sight.Visible;
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Equal(1, rig.Game.UpperSightCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_deferred_upper_sample_resumes_without_repeating_either_completed_ray(bool budgetExceeded)
    {
        var (rig, corpse) = Corpse();
        corpse.UpperSight = budgetExceeded ? Sight.Later : Sight.Unknown;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
        corpse.UpperSight = Sight.Visible;
        rig.ScanAfter(100);
        if (!budgetExceeded)
        {
            // Primary + low hit + upper Unknown already spent the three-attempt allowance. Do not cast a fourth ray.
            Assert.Empty(rig.Game.Loots);
            Assert.Equal(1, rig.Game.UpperSightCalls);
            Assert.Equal(0, rig.Loop.Counters.SightBlocked);
            rig.ScanAfter(LootLoop.SightCacheMs);
        }
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(budgetExceeded ? 1 : 2, rig.Game.PrimarySightCalls);
        Assert.Equal(budgetExceeded ? 1 : 2, rig.Game.BodySightCalls);
        Assert.Equal(2, rig.Game.UpperSightCalls);
    }

    [Fact]
    public void A_one_ray_per_step_allowance_finishes_all_three_stages_in_three_server_steps_not_three_scans()
    {
        var (rig, corpse) = Corpse();
        corpse.UpperSight = Sight.Visible;
        for (var i = 0; i < 3; i++)
        {
            rig.Game.RaysLeft = 1;
            rig.StepAfter(16);
        }
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Loop.Counters.Scans);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(3, rig.Game.ActualSightRays);
    }

    [Fact]
    public void A_permanently_deferred_lower_sample_cannot_pin_other_targets_in_the_scan()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Later;
        var twig = rig.Game.Add(LoopRig.Twig, 14.5, [], pickupItem: 1); // after the nearer corpse
        for (var i = 0; i < 3; i++) rig.StepAfter(16);
        Assert.Equal(1, rig.Loop.Counters.ScanCount);
        Assert.Contains(rig.Game.Loots, l => l.Host == twig.Id);
        Assert.Equal(2, rig.Game.PrimarySightCalls); // one corpse primary plus the twig
        Assert.Equal(3, rig.Game.BodySightCalls); // bounded to initial query + two next-step continuations
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
    }

    [Fact]
    public void Unknown_lower_samples_are_not_polled_every_server_tick()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Unknown;
        for (var i = 0; i < 6; i++) rig.StepAfter(16);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Equal(1, rig.Loop.Counters.ScanCount);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void A_large_set_of_unanswered_corpses_has_bounded_continuation_memory()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        for (var i = 0; i < 3000; i++)
        {
            var e = rig.Game.Add(LoopRig.Enemy, 2, [LootLoop.InventoryComponent]);
            e.Dead = true;
            e.Sight = Sight.Hidden;
            e.BodySight = Sight.Unknown;
        }
        rig.ScanAfter(100);
        var player = rig.Loop.Join(rig.Player);
        Assert.InRange(player.PendingSight.Count, 1, 2048);
        Assert.Empty(player.Sight);
        Assert.Empty(rig.Game.Loots);
        Assert.DoesNotContain(rig.KeyLines, l => l.Contains("not in plain view"));
    }

    [Fact]
    public void Switching_visibility_off_during_a_continuation_bypasses_all_remaining_samples()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Later;
        rig.StepAfter(16);
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { OpenDelay = 0, VisibleOnly = false });
        rig.StepAfter(16);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Equal(1, rig.Game.BodySightCalls);
        Assert.Equal(0, rig.Game.UpperSightCalls);
        Assert.Empty(rig.Loop.Join(rig.Player).PendingSight);
    }

    [Fact]
    public void An_exhausted_primary_ray_budget_has_only_two_next_step_continuations_then_releases_the_scan()
    {
        var (rig, _) = Corpse();
        rig.Game.RaysLeft = 0;
        for (var i = 0; i < 3; i++) rig.StepAfter(16);
        Assert.Equal(1, rig.Loop.Counters.ScanCount);
        Assert.Equal(3, rig.Game.PrimarySightCalls);
        Assert.Equal(0, rig.Game.ActualSightRays);
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void Repeated_unknown_answers_cannot_spend_more_than_three_ray_attempts_in_one_evaluation()
    {
        var (rig, corpse) = Corpse();
        corpse.BodySight = Sight.Unknown;
        rig.ScanFor(900);
        Assert.InRange(rig.Game.ActualSightRays, 1, 3);
        Assert.Equal(1, rig.Game.PrimarySightCalls);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal(0, rig.Loop.Counters.SightBlocked);
        Assert.Empty(rig.Loop.Join(rig.Player).Sight);
    }

}
