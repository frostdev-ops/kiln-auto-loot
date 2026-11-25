// SPDX-License-Identifier: MIT
// New world gear waits a moment before the salvage rules look at it; a mod that swaps it for a new copy right after it
// lands (Loot Leveler raises looted gear to the player's level that way) hands its world origin to the copy.
using Kiln;
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class SalvageSettleTests
{
    private static readonly EntityId Player = new(1), Other = new(2);
    private static EntityId E(uint id) => new(id);

    private static Func<EntityId, (SalvageKind, int)?> Facts(Dictionary<uint, (SalvageKind, int)> gear) =>
        e => gear.TryGetValue(e.Value, out var f) ? f : null;

    private static readonly Dictionary<uint, (SalvageKind, int)> Gear = new()
    {
        [10] = (SalvageKind.Weapons, 2),   // carried before
        [20] = (SalvageKind.Weapons, 2),   // the looted sword
        [21] = (SalvageKind.Weapons, 2),   // Loot Leveler's copy: same kind and rarity
        [22] = (SalvageKind.Weapons, 3),   // another rarity
        [23] = (SalvageKind.Rings, 2),     // another kind
        [24] = (SalvageKind.Weapons, 2),
        [30] = (SalvageKind.Armor, 1),
        [31] = (SalvageKind.Armor, 1),
    };

    [Fact]
    public void Gear_that_stays_waits_for_the_settle_time_then_goes_to_the_rules_once()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(10), E(20)]);
        Assert.True(s.Pending(Player));
        Assert.Empty(s.Poll(Player, [E(10), E(20)], Facts(Gear)));
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Equal([new SalvageSettle.Due(Player, E(20), "pickup", false)], s.Poll(Player, [E(10), E(20)], Facts(Gear)));
        Assert.False(s.Pending(Player));
        Assert.Empty(s.Poll(Player, [E(10), E(20)], Facts(Gear)));
    }

    [Fact]
    public void A_copy_that_replaces_the_looted_gear_counts_as_the_same_world_loot()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(10), E(20)]);
        clock.NowMs += SalvageSettle.SettleMs;
        // the sword is gone, a new one of the same kind and rarity is there; the one carried before is never taken
        Assert.Equal([new SalvageSettle.Due(Player, E(21), "pickup", true)], s.Poll(Player, [E(10), E(21)], Facts(Gear)));
    }

    [Fact]
    public void A_copy_that_comes_late_is_still_found_within_the_window_and_nothing_after_it()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "auto loot", [E(20)]);
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Empty(s.Poll(Player, [], Facts(Gear)));
        Assert.True(s.Pending(Player));
        clock.NowMs += 1000;
        Assert.Equal([new SalvageSettle.Due(Player, E(21), "auto loot", true)], s.Poll(Player, [E(21)], Facts(Gear)));

        s.Arrived(Player, E(30), SalvageKind.Armor, 1, "pickup", [E(21), E(30)]);
        clock.NowMs += SalvageSettle.WindowMs;
        Assert.Empty(s.Poll(Player, [E(21)], Facts(Gear)));
        Assert.False(s.Pending(Player));
        Assert.Empty(s.Poll(Player, [E(21), E(31)], Facts(Gear)));   // too late: the window is over
    }

    [Fact]
    public void Another_rarity_or_kind_is_never_a_replacement()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(20)]);
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Empty(s.Poll(Player, [E(22), E(23)], Facts(Gear)));
    }

    [Fact]
    public void Gear_the_player_threw_away_has_no_replacement()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(20)]);
        s.Tossed(E(20));
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Empty(s.Poll(Player, [E(21)], Facts(Gear)));
        Assert.False(s.Pending(Player));
    }

    [Fact]
    public void Gear_explained_another_way_or_still_waiting_itself_is_never_a_replacement()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(20)]);
        s.Explained(E(21));                                                  // crafted, or from the player's own chest
        clock.NowMs += 500;
        s.Arrived(Player, E(24), SalvageKind.Weapons, 2, "pickup", [E(20), E(21), E(24)]);
        clock.NowMs += SalvageSettle.SettleMs - 500;
        // 20 is gone; 21 is explained and 24 waits for its own settle time: neither replaces 20
        Assert.Empty(s.Poll(Player, [E(21), E(24)], Facts(Gear)));
        clock.NowMs += 500;
        Assert.Equal([new SalvageSettle.Due(Player, E(24), "pickup", false)], s.Poll(Player, [E(21), E(24)], Facts(Gear)));
    }

    [Fact]
    public void After_the_player_opens_a_chest_nothing_replaces_the_gear_that_left()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(20)]);
        clock.NowMs += 300;
        s.Opened(Player);   // the sword went into the player's own chest, a sword of theirs came out
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Empty(s.Poll(Player, [E(21)], Facts(Gear)));
        Assert.False(s.Pending(Player));
        // storage opened before the gear arrived does not stop it
        s.Arrived(Player, E(30), SalvageKind.Armor, 1, "by hand", [E(21), E(30)]);
        clock.NowMs += SalvageSettle.SettleMs;
        Assert.Equal([new SalvageSettle.Due(Player, E(31), "by hand", true)], s.Poll(Player, [E(21), E(31)], Facts(Gear)));
    }

    [Fact]
    public void Two_replaced_items_each_get_their_own_copy_and_players_are_apart()
    {
        var clock = new FakeClock();
        var s = new SalvageSettle(clock);
        s.Arrived(Player, E(20), SalvageKind.Weapons, 2, "pickup", [E(20)]);
        s.Arrived(Player, E(30), SalvageKind.Armor, 1, "pickup", [E(20), E(30)]);
        s.Arrived(Other, E(40), SalvageKind.Weapons, 2, "pickup", [E(40)]);
        clock.NowMs += SalvageSettle.SettleMs;
        var due = s.Poll(Player, [E(21), E(31)], Facts(Gear));
        Assert.Equal([E(21), E(31)], due.Select(d => d.Item).OrderBy(e => e.Value));
        Assert.All(due, d => Assert.True(d.Replaced));
        Assert.True(s.Pending(Other));
        Assert.Equal([E(40)], s.Poll(Other, [E(40)], Facts(Gear)).Select(d => d.Item));
    }
}
