// SPDX-License-Identifier: MIT
// Decor (TemplateFlags.Decor): furniture the world places that players can also build, the books of villages and ruins.
// A player's report: the mod took the old books lying in their base. The oldBooks switch is off by default, so they stay
// everywhere; on, they are taken out in the world but still stay in and near a base.
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class DecorTests
{
    private static readonly string[] Pickup = [];

    private static void NearBase(LoopRig rig, bool near) => rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { NearBase = near };

    [Fact]
    public void World_books_are_left_by_default_anywhere()
    {
        Assert.False(new Core.PersonalSettings().OldBooks);
        var rig = new LoopRig();
        rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        var twig = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Equal([twig.Id], rig.Game.Loots.Select(l => l.Host).Distinct().ToArray());   // other ground loot is taken
        Assert.True(rig.Loop.Counters.GroupOff > 0);
    }

    [Fact]
    public void With_old_books_on_they_stay_while_the_player_is_in_or_near_a_base()
    {
        var rig = new LoopRig(s => s.OldBooks = true);
        NearBase(rig, true);
        rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        var twig = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Equal([twig.Id], rig.Game.Loots.Select(l => l.Host).Distinct().ToArray());
        Assert.True(rig.Loop.Counters.DecorLeft > 0);
        Assert.Contains("decorLeft=", rig.Loop.Counters.CountersLine());
    }

    [Fact]
    public void With_old_books_on_they_are_ground_loot_away_from_a_base()
    {
        var rig = new LoopRig(s => s.OldBooks = true);
        var book = rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.Equal([book.Id], rig.Game.Loots.Select(l => l.Host).ToArray());
        Assert.Equal(0, rig.Loop.Counters.DecorLeft);
    }

    [Fact]
    public void The_base_check_is_made_on_every_scan()
    {
        var rig = new LoopRig(s => s.OldBooks = true);
        NearBase(rig, true);
        var book = rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        NearBase(rig, false);
        rig.ScanFor(3000);
        Assert.Contains(rig.Game.Loots, l => l.Host == book.Id);
    }

    [Fact]
    public void Ground_loot_off_leaves_books_even_with_old_books_on()
    {
        var rig = new LoopRig(s => { s.OldBooks = true; s.GroundLoot = false; });
        rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
    }
}
