// SPDX-License-Identifier: MIT
// Generic drops (TemplateFlags.Generic): what broken barrels, crates and pots and some resource nodes drop. The template
// names no item (the game sets it at the spawn), so the loop judges each by its live item (Core/GenericDrops).
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class GenericDropTests
{
    private static readonly string[] Pickup = [];

    /// <summary>The fixture items plus terrain stone (9) and a collection piece (10).</summary>
    private static ItemTable Items() => ItemTable.Parse(new StringReader(Fixtures.ItemsText +
        "9\tBuild_TerrainMaterial_T1_Stone\tStone\tStein\t1\tMaterials\tMaterials\n" +
        "10\tCollection_Test_Statue_Piece\tStatue Piece\t\t1\tCollectibles\tCollectibles\n"));

    [Fact]
    public void A_generic_drop_is_taken_as_a_pickup_of_its_live_item()
    {
        // a player's report: string, torn cloth and flint from smashed barrels lay on the ground, never taken
        var rig = new LoopRig(items: Items());
        rig.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 7);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);
        Assert.Equal(1, rig.Loop.Counters.GenericSeen);
        Assert.Equal(0, rig.Loop.Counters.UnknownTemplates);
    }

    [Fact]
    public void A_generic_drop_whose_item_is_skipped_stays_with_a_note()
    {
        var rig = new LoopRig(s => s.SkipList = "=Flint", items: Items());
        rig.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 7);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        Assert.Contains(rig.Notices, n => n.Msg.Kind == NoticeKind.LeftBehind && n.Msg.ItemId == 7);
    }

    [Fact]
    public void Stone_in_a_generic_drop_follows_the_stones_switch()
    {
        var off = new LoopRig(s => s.Stones = false, items: Items());
        off.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 9);
        var flint = off.Game.Add(LoopRig.Generic, 3, Pickup, pickupItem: 7);   // flint is no stone
        off.ScanFor(3000);
        Assert.Equal([flint.Id], off.Game.Loots.Select(l => l.Host).Distinct().ToArray());
        Assert.True(off.Loop.Counters.GroupOff > 0);

        var on = new LoopRig(items: Items());
        on.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 9);
        on.ScanAfter(100);
        Assert.Single(on.Game.Loots);
    }

    [Fact]
    public void Ground_loot_off_leaves_generic_drops()
    {
        var rig = new LoopRig(s => s.GroundLoot = false, items: Items());
        rig.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 7);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void A_quest_item_in_a_generic_drop_stays_a_collection_piece_follows_collectibles()
    {
        var rig = new LoopRig(s => s.Collectibles = false, quest: [4, 10], items: Items());
        rig.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 4);    // quest item: the player's own step
        rig.Game.Add(LoopRig.Generic, 3, Pickup, pickupItem: 10);   // collection piece, collectibles off
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.GenericLeft > 0);   // the quest item
        Assert.True(rig.Loop.Counters.GroupOff > 0);      // the collection piece

        var on = new LoopRig(quest: [10], items: Items());
        on.Game.Add(LoopRig.Generic, 3, Pickup, pickupItem: 10);
        on.ScanAfter(100);
        Assert.Single(on.Game.Loots);
    }

    [Fact]
    public void A_generic_drop_without_its_item_is_never_looted_unseen()
    {
        // no PickupItem (yet): the container path would send a loot-all that takes whatever it holds, skip list or not
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Flint"; }, items: Items());
        var e = rig.Game.Add(LoopRig.Generic, 2, [LootLoop.OfferComponent]);
        e.Offer = new Offer(true, false, 1);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.GenericLeft > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_only_list_and_gather_focus_judge_the_live_item(bool focus)
    {
        var rig = new LoopRig(s => { if (!focus) { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Flint"; } }, items: Items());
        if (focus)
        {
            rig.Loop.TookByHand(rig.Player, 7);   // flint taken by hand, then F11
            rig.Loop.Command(rig.Player, CommandKind.ToggleFocus);
        }
        var flint = rig.Game.Add(LoopRig.Generic, 2, Pickup, pickupItem: 7);
        rig.Game.Add(LoopRig.Generic, 3, Pickup, pickupItem: 6);
        rig.ScanFor(3000);
        Assert.Equal([flint.Id], rig.Game.Loots.Select(l => l.Host).Distinct().ToArray());
        Assert.True(focus ? rig.Loop.Counters.NotFocus > 0 : rig.Loop.Counters.NotListed > 0);
    }
}
