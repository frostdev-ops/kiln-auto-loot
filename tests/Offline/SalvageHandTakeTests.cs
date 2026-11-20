// SPDX-License-Identifier: MIT
// Gear a player takes by hand out of a world chest's screen (a drag, Take all) comes with no pickup note: the mod notes
// the chest's gear when the player opens it and finds those item entities in the backpack afterwards.
using Kiln;
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class SalvageHandTakeTests
{
    private static readonly EntityId Player = new(1), Chest = new(500), Other = new(501);
    private static EntityId E(uint id) => new(id);

    [Fact]
    public void Gear_taken_by_hand_from_an_opened_world_chest_arrives_once()
    {
        var clock = new FakeClock();
        var h = new SalvageHandTakes(clock);
        h.Backpack(Player, [E(10)]);                          // what the player carried before
        h.Opened(Player, Chest, [E(20), E(21)]);
        Assert.True(h.Watching(Player));
        Assert.Empty(h.Backpack(Player, [E(10)]));
        Assert.Equal([E(20)], h.Backpack(Player, [E(10), E(20)]));   // dragged one
        Assert.Empty(h.Backpack(Player, [E(10), E(20)]));            // never twice
        Assert.Equal([E(21)], h.Backpack(Player, [E(10), E(20), E(21)]));
    }

    [Fact]
    public void Gear_the_player_carried_before_never_counts_even_after_a_trip_through_the_chest()
    {
        var clock = new FakeClock();
        var h = new SalvageHandTakes(clock);
        h.Backpack(Player, [E(10)]);
        // the player put their own sword into the world chest, closed it, opened it again
        h.Opened(Player, Chest, [E(10), E(20)]);
        Assert.Equal([E(20)], h.Backpack(Player, [E(10), E(20)]));
    }

    [Fact]
    public void The_watch_ends_after_its_window_and_another_players_bag_does_not_count()
    {
        var clock = new FakeClock();
        var h = new SalvageHandTakes(clock);
        h.Opened(Player, Chest, [E(20)]);
        Assert.Empty(h.Backpack(new EntityId(2), [E(20)]));
        clock.NowMs += SalvageHandTakes.WindowMs + 1;
        Assert.False(h.Watching(Player));
        Assert.Empty(h.Backpack(Player, [E(20)]));
    }

    [Fact]
    public void Two_chests_are_watched_together()
    {
        var clock = new FakeClock();
        var h = new SalvageHandTakes(clock);
        h.Opened(Player, Chest, [E(20)]);
        h.Opened(Player, Other, [E(30)]);
        Assert.Equal([E(20), E(30)], h.Backpack(Player, [E(30), E(20)]).OrderBy(x => x.Value));
        h.Forget(Player);
        Assert.False(h.Watching(Player));
    }
}
