// SPDX-License-Identifier: MIT
// After 0.2.6: a player (Nexus) had Fish on and saw no fish taken, and no line about fish in kiln.log, even swimming
// through a fishing spot. The fish of a fishing spot are not in the water until one bites the rod (the game makes the
// fish from the spot's fish table, and its item only once caught), so there is nothing to take; the fish swimming free
// (mostly in the Veilwater Basin's lakes) are pickups a swimming player grabs with E, and the mod takes those. With Fish on,
// kiln.log now has one line per fish a player passes (taken or why not, and whether the game handed it over) and one
// per fishing spot in reach, saying its fish come only with a rod.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class FishTests
{
    private const uint Shimmerfin = 50;
    private static readonly Guid Spot = new("11111111-0000-0000-0000-0000000000f5");
    private static readonly string[] Swimming = [LootLoop.OfferComponent];   // the game's pickup: offer, PickupItem

    private static ItemTable Items() => ItemTable.Parse(new StringReader(Fixtures.ItemsText +
        "50\t_Food_raw_bad_T1_fish_common_01\tShimmerfin\tSchimmerflosse\t1\tConsumables\tConsumables\n"));

    private static LoopRig Rig(Action<PersonalSettings>? settings = null)
    {
        var rig = new LoopRig(s => { s.Fish = true; settings?.Invoke(s); }, items: Items());
        rig.Game.TemplateNames[Spot] = "_FishingLocation_Generic_MoreFish";
        return rig;
    }

    private static string[] Fish(LoopRig rig) => rig.KeyLines.Where(l => l.StartsWith("fish:", StringComparison.Ordinal)).ToArray();

    private static FakeEntity AddFish(LoopRig rig, double x, double y = 0) => rig.Game.Add(LoopRig.Fish, x, Swimming, Shimmerfin, y);

    private static FakeEntity AddSpot(LoopRig rig, double x) => rig.Game.Add(Spot, x, [LootLoop.FishingSpotComponent]);

    [Fact]
    public void A_fish_swimming_free_is_grabbed_as_a_pickup_and_one_line_says_so()
    {
        var rig = Rig();
        var fish = AddFish(rig, 6, y: -2);
        rig.ScanAfter(100);
        Assert.Contains(rig.Game.Loots, l => l.Host == fish.Id);
        Assert.Empty(rig.Game.Interacts);   // no accept: the pickup path, as E on it
        rig.Game.Remove(fish);              // the game handed it over
        rig.ScanFor(3000);
        var line = Assert.Single(Fish(rig));
        Assert.Contains("Animal_wildlife_Fish_T1_common_01_Test", line);
        Assert.Contains("loot sent", line);
        Assert.Contains("_Food_raw_bad_T1_fish_common_01", line);
    }

    [Fact]
    public void Off_fish_stay_and_kiln_log_stays_quiet()
    {
        var rig = new LoopRig(items: Items());
        Assert.False(new PersonalSettings().Fish);
        var fish = AddFish(rig, 6);
        AddSpot(rig, 8);
        rig.ScanFor(3000);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == fish.Id);
        Assert.Empty(Fish(rig));
        Assert.False(rig.Game.FindCalls.ContainsKey(LootLoop.FishingSpotComponent));   // no search for fishing spots
    }

    [Fact]
    public void A_fish_left_says_why_once()
    {
        var deep = Rig();   // the depth window: 4 m below by default
        AddFish(deep, 3, y: -7);
        deep.ScanFor(3000);
        Assert.Empty(deep.Game.Loots);
        Assert.Contains("7.0 m below you, deeper than Depth below me (4 m)", Assert.Single(Fish(deep)));

        var far = Rig(s => { s.Radius = 10; s.GroundRadius = 30; });   // the search reaches 30 m, fish the 10 m main radius
        AddFish(far, 20);
        far.ScanFor(3000);
        Assert.Empty(far.Game.Loots);
        Assert.Contains("beyond the main radius (10 m)", Assert.Single(Fish(far)));

        var skipped = Rig(s => s.SkipList = "=_Food_raw_bad_T1_fish_common_01");
        AddFish(skipped, 3);
        skipped.ScanFor(3000);
        Assert.Empty(skipped.Game.Loots);
        Assert.Contains("the fish is on the skip list", Assert.Single(Fish(skipped)));
    }

    [Fact]
    public void A_fish_still_there_after_its_take_says_the_game_did_not_hand_it_over()
    {
        var rig = Rig();
        AddFish(rig, 4);
        rig.ScanFor(3000);
        var lines = Fish(rig);
        Assert.Equal(2, lines.Length);
        Assert.Contains("loot sent", lines[0]);
        Assert.Contains("the game did not hand the fish over", lines[1]);
    }

    [Fact]
    public void A_fishing_spot_in_reach_says_once_that_its_fish_come_only_with_a_rod()
    {
        var rig = Rig();
        AddSpot(rig, 8);
        AddSpot(rig, 40);   // beyond the 15 m main radius: no line
        rig.ScanFor(6000);
        var line = Assert.Single(Fish(rig));
        Assert.Contains("8.0 m from a fishing spot (_FishingLocation_Generic_MoreFish)", line);
        Assert.Contains("only with a fishing rod", line);
        Assert.Empty(rig.Game.Loots);
        // looked for every 2 s, not every scan
        Assert.InRange(rig.Game.FindCalls[LootLoop.FishingSpotComponent], 1, 4);
    }
}
