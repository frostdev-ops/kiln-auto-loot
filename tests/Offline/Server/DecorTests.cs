// SPDX-License-Identifier: MIT
// Decor (TemplateFlags.Decor): furniture the world places that players can also build, the books of villages and ruins.
// A player's report: the mod took the old books lying in their base. In or near a base they stay; elsewhere they are
// ground loot as before.
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class DecorTests
{
    private static readonly string[] Pickup = [];

    private static void NearBase(LoopRig rig, bool near) => rig.Game.PlayerList[0] = rig.Game.PlayerList[0] with { NearBase = near };

    [Fact]
    public void World_books_stay_while_the_player_is_in_or_near_a_base()
    {
        var rig = new LoopRig();
        NearBase(rig, true);
        rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        var twig = rig.Game.Add(LoopRig.Twig, 3, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Equal([twig.Id], rig.Game.Loots.Select(l => l.Host).Distinct().ToArray());   // other ground loot is taken
        Assert.True(rig.Loop.Counters.DecorLeft > 0);
        Assert.Contains("decorLeft=", rig.Loop.Counters.CountersLine());
    }

    [Fact]
    public void World_books_away_from_a_base_are_ground_loot()
    {
        var rig = new LoopRig();
        var book = rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        rig.ScanAfter(100);
        Assert.Equal([book.Id], rig.Game.Loots.Select(l => l.Host).ToArray());
        Assert.Equal(0, rig.Loop.Counters.DecorLeft);
    }

    [Fact]
    public void The_base_check_is_made_on_every_scan()
    {
        var rig = new LoopRig();
        NearBase(rig, true);
        var book = rig.Game.Add(LoopRig.Book, 2, Pickup, pickupItem: 1);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        NearBase(rig, false);
        rig.ScanFor(3000);
        Assert.Contains(rig.Game.Loots, l => l.Host == book.Id);
    }
}
