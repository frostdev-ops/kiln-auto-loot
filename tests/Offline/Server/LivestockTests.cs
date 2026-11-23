// SPDX-License-Identifier: MIT
// Farm animal produce (animalProduce): a grown farm animal keeps its produce (milk, wool) in its own inventory, which
// the game refills in minutes. The mod takes it with a loot-all (the game's own pickup note) or slot moves when
// something stays, never with an accept: the animal's offer upgrades a baby and pets an animal with nothing to give.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class LivestockTests
{
    private const uint Apple = 6, Flint = 7;   // fixture items standing in for milk and wool

    private static LoopRig Rig(Action<PersonalSettings>? settings = null) =>
        new(s => { s.AnimalProduce = true; settings?.Invoke(s); });

    private static FakeEntity Goat(LoopRig rig, double x = 4, params (uint Item, int Count)[] produce)
    {
        var goat = rig.Game.Add(LoopRig.Goat, x, LoopRig.OfferInv);
        goat.Offer = new Offer(true, false, 1);
        goat.Slots = produce.Select((p, i) => new ContainerSlot((ulong)i + 1, p.Item, p.Count)).ToList();
        return goat;
    }

    private static int LootsOf(LoopRig rig, FakeEntity e) => rig.Game.Loots.Count(l => l.Host == e.Id);

    [Fact]
    public void Off_by_default()
    {
        Assert.False(new PersonalSettings().AnimalProduce);
        var rig = new LoopRig();
        var goat = Goat(rig, produce: (Apple, 8));
        rig.ScanFor(3000);
        Assert.Equal(0, LootsOf(rig, goat));
        Assert.Empty(rig.Game.Moves);
        Assert.True(rig.Loop.Counters.GroupOff > 0);
    }

    [Fact]
    public void Produce_is_taken_with_a_loot_all_at_once_and_never_an_accept()
    {
        var rig = Rig(s => s.OpenDelay = 1);
        var goat = Goat(rig, produce: (Apple, 8));
        rig.ScanAfter(100);
        Assert.Equal(1, LootsOf(rig, goat));   // no open delay, no accept first
        Assert.Empty(rig.Game.Interacts);
        rig.ScanFor(10_000);
        Assert.Empty(rig.Game.Interacts);
    }

    [Fact]
    public void An_animal_with_nothing_ready_is_left_alone()
    {
        var rig = Rig();
        var goat = Goat(rig);
        rig.ScanFor(10_000);
        Assert.Equal(0, LootsOf(rig, goat));
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Moves);
    }

    [Fact]
    public void It_is_read_once_per_interval_and_taken_again_when_it_refills()
    {
        var rig = Rig();
        var goat = Goat(rig, produce: (Apple, 2));
        rig.ScanAfter(100);
        Assert.Equal(1, LootsOf(rig, goat));
        goat.Slots = [];   // the loot-all emptied it
        rig.ScanFor(LootLoop.LivestockEveryMs - 200);
        Assert.Equal(1, rig.Loop.Counters.LivestockReads);   // not read again within the interval
        goat.Slots = [new ContainerSlot(1, Apple, 1)];   // refilled
        rig.ScanFor(LootLoop.LivestockEveryMs);
        Assert.Equal(2, LootsOf(rig, goat));
    }

    [Fact]
    public void Skipped_produce_stays_and_the_rest_is_moved()
    {
        var rig = Rig(s => s.SkipList = "=Flint");
        var goat = Goat(rig, produce: [(Apple, 2), (Flint, 3)]);
        rig.ScanAfter(100);
        Assert.Equal(0, LootsOf(rig, goat));   // a loot-all would take the flint too
        var move = Assert.Single(rig.Game.Moves);
        Assert.Equal((goat.Id, 1UL), (move.From, move.Slot));
        Assert.Empty(rig.Game.Interacts);
    }

    [Fact]
    public void Animals_follow_the_main_radius()
    {
        var rig = Rig(s => { s.Radius = 10; s.ChestsRadius = 30; s.CropsRadius = 30; });
        var near = Goat(rig, 8, (Apple, 2));
        var far = Goat(rig, 12, (Apple, 2));
        rig.ScanFor(500);
        Assert.Equal(1, LootsOf(rig, near));
        Assert.Equal(0, LootsOf(rig, far));
    }
}
