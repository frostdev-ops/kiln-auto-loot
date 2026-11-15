// SPDX-License-Identifier: MIT
// The loot loop (src/Server/LootLoop.cs) against a fake world: containers, pickups, enemy pause, the window pause, the slot filter, hold-E, scan rate.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class LootLoopTests
{
    private static readonly string[] Pickup = [];

    [Fact]
    public void A_corpse_waits_lootDelay_then_is_accepted_and_looted_in_a_later_scan_once_the_offer_changes()
    {
        var rig = new LoopRig();
        var corpse = rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(1, 2, 3)];

        rig.ScanAfter(100);                       // first seen
        rig.ScanFor(800);                         // still inside openDelay 1.0 s
        Assert.Empty(rig.Game.Interacts);

        rig.ScanFor(200);                         // 1.0 s after the first sighting
        Assert.Single(rig.Game.Interacts);
        Assert.Equal((rig.Player, corpse.Id, 5u), rig.Game.Interacts[0]);
        Assert.Empty(rig.Game.Loots);             // never in the scan of the accept

        rig.ScanAfter(100);                       // offer id unchanged: wait
        Assert.Empty(rig.Game.Loots);

        corpse.Offer = new Offer(true, false, 6); // the game reacted
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);
        Assert.Single(rig.Game.Interacts);
    }

    [Fact]
    public void An_unchanged_offer_is_accepted_again_after_3_s_up_to_3_times_then_looted_anyway()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var corpse = rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(1, 2, 3)];

        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);
        rig.ScanFor(3000);                        // exactly 3 s: not yet
        Assert.Single(rig.Game.Interacts);
        rig.ScanAfter(100);                       // over 3 s
        Assert.Equal(2, rig.Game.Interacts.Count);
        rig.ScanFor(3100);
        Assert.Equal(3, rig.Game.Interacts.Count);
        Assert.Empty(rig.Game.Loots);
        rig.ScanFor(3100);                        // the third accept was refused too: loot anyway
        Assert.Equal(3, rig.Game.Interacts.Count);
        Assert.NotEmpty(rig.Game.Loots);
    }

    [Fact]
    public void A_chest_with_a_quest_item_gets_Items_Move_per_other_slot_and_the_quest_item_stays()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0, quest: [4]);
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);   // no offer: direct loot
        chest.Slots = [new ContainerSlot(10, 4, 1), new ContainerSlot(11, 2, 5), new ContainerSlot(12, 6, 1)];

        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        Assert.Equal([11ul, 12ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
        Assert.All(rig.Game.Moves, m => Assert.Equal((chest.Id, rig.Player), (m.From, m.To)));

        rig.ScanAfter(100);                       // moves still in flight: nothing new
        Assert.Equal(2, rig.Game.Moves.Count);

        foreach (var m in rig.Game.Moves.ToList()) m.Done(MoveOutcome.Done);
        chest.Slots = [new ContainerSlot(10, 4, 1)];
        rig.Game.Clear();
        rig.ScanFor(500);                         // only the quest item is left: no loot-all, no move, no notice
        Assert.Empty(rig.Game.Moves);
        Assert.Empty(rig.Game.Loots);
        Assert.Empty(rig.Notices);
    }

    [Fact]
    public void A_chest_without_kept_items_gets_one_loot_all()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0, quest: [4]);
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);
        chest.Slots = [new ContainerSlot(11, 2, 5)];
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);
        Assert.Empty(rig.Game.Moves);
    }

    [Fact]
    public void A_chest_item_on_the_ignore_list_stays_and_is_noticed()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; });
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);
        chest.Slots = [new ContainerSlot(11, 2, 5), new ContainerSlot(12, 6, 1)];
        rig.ScanAfter(100);
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
        Assert.Contains(rig.Notices, n => n.Msg.Kind == NoticeKind.LeftBehind && n.Msg.ItemId == 6);
    }

    [Fact]
    public void A_bush_whose_yield_is_ignored_is_never_accepted_or_harvested()
    {
        var rig = new LoopRig(s => s.SkipList = "=Plant Fiber");
        var bush = rig.Game.Add(LoopRig.Bush, 2, LoopRig.OfferInv);
        bush.Offer = new Offer(true, false, 9);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Loots);
        Assert.Single(rig.Notices, n => n.Msg.Kind == NoticeKind.LeftBehind && n.Msg.ItemId == 3);
    }

    [Fact]
    public void A_plant_is_harvested_once_per_visit_also_without_an_offer()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        rig.Game.Add(LoopRig.Bush, 2, [LootLoop.InventoryComponent]);
        rig.ScanFor(3000);
        Assert.Single(rig.Game.Loots);
    }

    [Fact]
    public void A_stuck_pickup_backs_off_for_10_s_and_raises_inventory_full_once()
    {
        var rig = new LoopRig();
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);   // never disappears: the inventory is full

        rig.ScanFor(3300);
        var tries = rig.Game.Loots.Count;
        Assert.InRange(tries, 2, 3);              // one take per 1500 ms
        Assert.Single(rig.Notices, n => n.Msg.Kind == NoticeKind.InventoryFull);

        rig.ScanFor(9500);                        // backing off
        Assert.Equal(tries, rig.Game.Loots.Count);

        rig.ScanFor(1500);                        // a fresh try after 10 s
        Assert.True(rig.Game.Loots.Count > tries);
        Assert.Single(rig.Notices, n => n.Msg.Kind == NoticeKind.InventoryFull);   // 60 s gap
    }

    [Fact]
    public void An_ignored_pickup_stays_without_a_full_notice()
    {
        var rig = new LoopRig(s => s.SkipList = "=Twigs");
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        rig.ScanFor(15000);
        Assert.Empty(rig.Game.Loots);
        Assert.DoesNotContain(rig.Notices, n => n.Msg.Kind == NoticeKind.InventoryFull);
        Assert.Contains(rig.Notices, n => n.Msg.Kind == NoticeKind.LeftBehind && n.Msg.ItemId == 1);
    }

    [Fact]
    public void The_window_flag_stops_everything()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var corpse = rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);

        rig.Loop.SetWindowOpen(rig.Player, true);
        rig.ScanFor(2000);
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.WindowPauses > 0);

        rig.Loop.SetWindowOpen(rig.Player, false);
        rig.ScanAfter(100);
        Assert.NotEmpty(rig.Game.Interacts);
        Assert.NotEmpty(rig.Game.Loots);
    }

    [Fact]
    public void A_player_without_the_mod_pauses_on_the_games_own_ui_state()
    {
        var rig = new LoopRig();
        var other = new EntityId(9);
        rig.Game.PlayerList.Add(new GamePlayer(other, new WorldPosition(100, 0, 0), UiOpen: true));
        rig.Game.Add(LoopRig.Twig, 101, Pickup, pickupItem: 1);
        rig.ScanFor(500);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Guest == other);

        rig.Game.PlayerList[1] = rig.Game.PlayerList[1] with { UiOpen = false };
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Guest == other);
    }

    [Fact]
    public void Autoloot_off_sends_nothing()
    {
        var rig = new LoopRig(s => s.Looting = false);
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void At_most_64_injects_per_scan_the_rest_waits_for_the_next()
    {
        var rig = new LoopRig();
        for (var i = 0; i < 100; i++) rig.Game.Add(LoopRig.Twig, 1 + i * 0.1, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.Equal(LootLoop.MaxInjectsPerScan, rig.Game.Loots.Count);
        rig.ScanAfter(100);
        Assert.Equal(100, rig.Game.Loots.Select(l => l.Host).Distinct().Count());
    }

    [Fact]
    public void The_cap_is_shared_by_all_players()
    {
        var rig = new LoopRig();
        var second = rig.Game.AddPlayer(x: 1);
        for (var i = 0; i < 50; i++) rig.Game.Add(LoopRig.Twig, 1 + i * 0.01, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.Equal(LootLoop.MaxInjectsPerScan, rig.Game.Loots.Count);
        Assert.Contains(rig.Game.Loots, l => l.Guest == second);
    }

    [Fact]
    public void Each_component_is_found_once_per_scan_for_all_players()
    {
        var rig = new LoopRig();
        rig.Game.AddPlayer(x: 5);
        rig.Game.AddPlayer(x: 9);
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.All(rig.Game.FindCalls.Values, n => Assert.Equal(1, n));
    }

    [Fact]
    public void Step_scans_every_100_ms_by_the_clock()
    {
        var rig = new LoopRig();
        for (var i = 0; i < 10; i++)
        {
            rig.Clock.NowMs += 40;                // a 25 Hz server
            rig.Loop.Step();
        }
        Assert.InRange(rig.Loop.Counters.Scans, 4, 5);
    }

    [Fact]
    public void A_living_enemy_pauses_looting_until_2_s_after_it_died_and_an_animal_never_does()
    {
        var rig = new LoopRig(s => { s.EnemyPause = true; s.EnemyDistance = 10; });
        rig.Game.Add(LoopRig.Wolf, 3, [LootLoop.EnemyComponent]);
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);            // an animal: no pause

        var enemy = rig.Game.Add(LoopRig.Enemy, 8, [LootLoop.EnemyComponent]);
        rig.Game.Clear();
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.EnemyPauses > 0);

        enemy.Dead = true;
        rig.ScanFor(1900);
        Assert.Empty(rig.Game.Loots);
        rig.ScanFor(300);
        Assert.NotEmpty(rig.Game.Loots);
    }

    [Fact]
    public void The_enemy_radius_is_exact_and_not_capped_by_the_loot_radius()
    {
        var rig = new LoopRig(s => { s.EnemyPause = true; s.EnemyDistance = 30; s.Radius = 5; });
        rig.Game.Add(LoopRig.Enemy, 25, [LootLoop.EnemyComponent]);
        rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        rig.ScanFor(500);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void A_dead_enemy_is_looted_as_a_corpse_and_a_living_one_never()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var enemy = rig.Game.Add(LoopRig.Enemy, 3, [LootLoop.OfferComponent, LootLoop.InventoryComponent]);
        enemy.Offer = new Offer(true, false, 1);
        enemy.Slots = [new ContainerSlot(1, 2, 1)];
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Interacts);

        enemy.Dead = true;
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);
    }

    [Fact]
    public void A_pickup_with_an_inventory_is_accepted_and_looted_in_the_same_scan_on_its_first_sighting()
    {
        var rig = new LoopRig();
        var shroom = rig.Game.Add(LoopRig.Mushroom, 3, LoopRig.OfferInv, pickupItem: 6);
        shroom.Offer = new Offer(true, false, 2);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);
        Assert.Single(rig.Game.Loots);
        rig.ScanFor(2000);                        // no second accept in the visit
        Assert.Single(rig.Game.Interacts);
    }

    [Fact]
    public void Without_instant_loot_a_static_pickup_is_only_taken_within_075_m()
    {
        var rig = new LoopRig(s => s.TakeInPlace = false);
        var shroom = rig.Game.Add(LoopRig.Mushroom, 3, [LootLoop.InventoryComponent], pickupItem: 6);
        var twig = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == shroom.Id);
        Assert.Contains(rig.Game.Loots, l => l.Host == twig.Id);   // movable: still taken

        shroom.Position = new WorldPosition(0.5, 0, 0);
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == shroom.Id);
    }

    [Fact]
    public void Unknown_and_boss_templates_are_never_taken()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        rig.Game.Add(LoopRig.Unknown, 2, Pickup, pickupItem: 1);
        rig.Game.Add(LoopRig.Boss, 2, Pickup, pickupItem: 7);
        rig.ScanFor(1000);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.UnknownTemplates > 0);
    }

    [Fact]
    public void A_target_out_of_its_group_radius_is_left()
    {
        var rig = new LoopRig(s => s.GroundRadius = 3);
        rig.Game.Add(LoopRig.Twig, 5, Pickup, pickupItem: 1);
        rig.ScanFor(500);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void Line_of_sight_hides_a_target_when_required_and_unknown_counts_as_visible()
    {
        var rig = new LoopRig(s => s.VisibleOnly = true);
        var hidden = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        hidden.Sight = Sight.Hidden;
        var unknown = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: 1);
        unknown.Sight = Sight.Unknown;
        rig.ScanAfter(100);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == hidden.Id);
        Assert.Contains(rig.Game.Loots, l => l.Host == unknown.Id);
    }

    [Fact]
    public void A_reused_entity_id_starts_a_new_visit()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var corpse = rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(1, 2, 3)];
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);

        rig.Game.Remove(corpse);
        rig.ScanAfter(100);                       // gone for a scan
        corpse.Serial = 2;                        // a new corpse with the old id
        rig.Game.Entities[corpse.Id] = corpse;
        rig.ScanAfter(100);
        Assert.Equal(2, rig.Game.Interacts.Count);
    }

    [Fact]
    public void The_servers_limits_cap_a_players_radius()
    {
        var server = new ServerSettings();
        server.Limits.MaxRadius = 5;
        var rig = new LoopRig(s => s.Radius = 40, server: server);
        rig.Game.Add(LoopRig.Twig, 20, Pickup, pickupItem: 1);
        rig.ScanFor(500);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.SettingsOf(rig.Player).Radius <= 5);
    }

    [Fact]
    public void A_player_without_settings_gets_the_servers_defaults()
    {
        var server = new ServerSettings();
        server.Defaults.GroundLoot = false;
        var rig = new LoopRig(server: server);
        var other = rig.Game.AddPlayer(x: 100);
        rig.Game.Add(LoopRig.Twig, 101, Pickup, pickupItem: 1);
        rig.ScanFor(500);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Guest == other);
        Assert.False(rig.Loop.SettingsOf(other).GroundLoot);
    }

    [Fact]
    public void Reset_drops_every_player()
    {
        var rig = new LoopRig();
        rig.Loop.Reset();
        Assert.Empty(rig.Loop.Players);
    }

    // ---------------------------------------------------------------- hold-E guard

    [Fact]
    public void Hold_E_is_canceled_for_a_group_that_is_off()
    {
        var rig = new LoopRig(s => s.GroundLoot = false);
        var twig = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        Assert.True(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, null));
    }

    [Fact]
    public void Hold_E_is_canceled_out_of_the_group_radius()
    {
        var rig = new LoopRig(s => s.GroundRadius = 2);
        var twig = rig.Game.Add(LoopRig.Twig, 4, Pickup, pickupItem: 1);
        Assert.True(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, null));
        Assert.False(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, new WorldPosition(1, 0, 0)));
    }

    [Fact]
    public void Hold_E_is_canceled_for_a_pickup_whose_item_is_ignored()
    {
        var rig = new LoopRig(s => s.SkipList = "=Twigs");
        var twig = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        Assert.True(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, null));
    }

    [Fact]
    public void Hold_E_on_a_chest_of_a_switched_off_group_is_canceled()
    {
        var rig = new LoopRig(s => s.Chests = false);
        var chest = rig.Game.Add(LoopRig.Chest, 2, [LootLoop.InventoryComponent]);
        Assert.True(rig.Loop.ShouldCancelHoldE(rig.Player, chest.Id, null));
    }

    [Fact]
    public void Hold_E_is_left_alone_for_allowed_targets_bosses_living_enemies_and_unknown_templates()
    {
        var rig = new LoopRig();
        var twig = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        var boss = rig.Game.Add(LoopRig.Boss, 2, Pickup, pickupItem: 7);
        var enemy = rig.Game.Add(LoopRig.Enemy, 2, [LootLoop.InventoryComponent]);
        var unknown = rig.Game.Add(LoopRig.Unknown, 2, Pickup, pickupItem: 1);
        Assert.False(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, null));
        Assert.False(rig.Loop.ShouldCancelHoldE(rig.Player, boss.Id, null));
        Assert.False(rig.Loop.ShouldCancelHoldE(rig.Player, enemy.Id, null));
        Assert.False(rig.Loop.ShouldCancelHoldE(rig.Player, unknown.Id, null));
    }

    [Fact]
    public void Hold_E_follows_the_rules_also_with_autoloot_off()
    {
        var rig = new LoopRig(s => { s.Looting = false; s.GroundLoot = false; });
        var twig = rig.Game.Add(LoopRig.Twig, 2, Pickup, pickupItem: 1);
        Assert.True(rig.Loop.ShouldCancelHoldE(rig.Player, twig.Id, null));
    }
}

public sealed class UnreadableInventoryTests
{
    [Theory]
    [InlineData("chest")]
    [InlineData("corpse")]
    public void A_container_whose_inventory_cannot_be_read_is_left_and_tried_again(string kind)
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; }, quest: [4]);
        var e = kind == "chest"
            ? rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent])
            : rig.Game.Add(LoopRig.Corpse, 3, LoopRig.OfferInv);
        if (kind == "corpse") e.Offer = new Offer(true, false, 5);
        e.Slots = null;                                   // kiln.items off, or the read failed
        rig.ScanFor(3000);
        if (kind == "corpse") e.Offer = new Offer(true, false, 6);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        Assert.Empty(rig.Game.Moves);
        Assert.True(rig.Loop.Counters.Unreadable > 0);

        e.Slots = [new ContainerSlot(10, 4, 1), new ContainerSlot(11, 2, 5), new ContainerSlot(12, 6, 1)];
        rig.ScanFor(300);                                 // readable again: the filter runs
        Assert.Empty(rig.Game.Loots);
        Assert.Equal([11ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    [Fact]
    public void A_fog_nest_crystal_gets_its_loot_all_although_its_inventory_cannot_be_read()
    {
        // the crystal has a DefaultInventory and no Inventory: the read fails, and only the loot-all hands its essence over
        // (the accept alone moves its offer on); found in a hosted game with the probe build
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var crystal = rig.Game.Add(LoopRig.FogNest, 3, [LootLoop.OfferComponent]);
        crystal.Offer = new Offer(true, false, 1);
        crystal.Slots = null;
        rig.ScanFor(300);
        Assert.Single(rig.Game.Interacts);
        crystal.Offer = new Offer(true, false, 2);        // the accept moved the offer on
        rig.ScanFor(3000);
        Assert.Single(rig.Game.Loots);                    // once per visit, also when the bag was full
        Assert.Equal(0, rig.Loop.Counters.Unreadable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_readable_ground_container_retries_a_slot_move_that_did_not_finish(bool noRoom)
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CarryLimits = "=Stone:3"; });
        var crystal = rig.Game.Add(LoopRig.FogNest, 3, [LootLoop.InventoryComponent]);
        crystal.Slots = [new ContainerSlot(11, 2, 5)];
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        Assert.Equal(3, Assert.Single(rig.Game.MoveCounts));
        move.Done(noRoom ? MoveOutcome.NoRoom : MoveOutcome.Failed);

        rig.Game.Clear();
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Moves);
        Assert.Equal(3, Assert.Single(rig.Game.MoveCounts));
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void A_readable_ground_container_can_give_the_rest_after_a_partial_take()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CarryLimits = "=Stone:3"; });
        var crystal = rig.Game.Add(LoopRig.FogNest, 3, [LootLoop.InventoryComponent]);
        crystal.Slots = [new ContainerSlot(11, 2, 5)];
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        Assert.Equal(3, Assert.Single(rig.Game.MoveCounts));
        crystal.Slots = [new ContainerSlot(11, 2, 2)];
        rig.Game.BagSlots = [new BagSlot(2, 3, true)];
        move.Done(MoveOutcome.Done);
        rig.Game.Clear();
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Moves);
        Assert.Empty(rig.Game.Loots);

        rig.Game.BagSlots = [];   // the player used the stone while still next to the crystal
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);
    }

    [Fact]
    public void A_plant_is_still_harvested_without_reading_its_inventory()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var bush = rig.Game.Add(LoopRig.Bush, 2, [LootLoop.InventoryComponent]);
        bush.Slots = null;
        rig.ScanFor(1000);
        Assert.Single(rig.Game.Loots);
    }
}
