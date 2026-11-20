// SPDX-License-Identifier: MIT
// 0.2.4: a player reported fishing bait still not picked up on 0.2.3. The loop now writes one kiln.log line per critter
// (fishing bait swarm) a player passes: taken or why not, and whether the game handed the bait over.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class CritterLogTests
{
    private static readonly string[] Swarm = [LootLoop.PickupComponent, LootLoop.OfferComponent];

    private static ItemTable Items() => ItemTable.Parse(new StringReader(Fixtures.ItemsText +
        "40\tMaterial_T6_Frogs_FishingBait\tFrog\tFrosch\t1\tAmmunition\tAmmunition\n"));

    private static LoopRig Rig(Action<PersonalSettings>? settings = null) => new(settings, items: Items());

    private static string[] Critter(LoopRig rig) => rig.KeyLines.Where(l => l.StartsWith("critter:", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void A_critter_taken_gets_one_line_naming_it_and_the_take()
    {
        var rig = Rig();
        rig.Game.Add(LoopRig.Frogs, 3, Swarm);
        rig.ScanFor(1000);
        var line = Assert.Single(Critter(rig));
        Assert.Contains("Lootpickup_Scatter_VFX_Bait_Frogs_Test", line);
        Assert.Contains("loot sent", line);
        Assert.Contains("Material_T6_Frogs_FishingBait", line);
    }

    [Fact]
    public void A_critter_left_says_why_once()
    {
        var off = Rig(s => s.Critters = false);
        off.Game.Add(LoopRig.Frogs, 3, Swarm);
        off.ScanFor(1000);
        Assert.Contains("Critters is off", Assert.Single(Critter(off)));

        var high = Rig();
        high.Game.Add(LoopRig.Frogs, 0, Swarm, y: 9);   // 9 m above: within the radius, outside the 4 m height window
        high.ScanFor(1000);
        Assert.Contains("height window", Assert.Single(Critter(high)));

        var far = Rig(s => { s.Radius = 5; s.GroundRadius = 20; });   // the search reaches 20 m, critters only 5
        far.Game.Add(LoopRig.Frogs, 8, Swarm);
        far.ScanFor(1000);
        Assert.Contains("beyond the main radius (5 m)", Assert.Single(Critter(far)));

        var focus = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Material_T1_Twigs"; });
        focus.Game.Add(LoopRig.Frogs, 3, Swarm);
        focus.ScanFor(1000);
        Assert.Contains("only list", Assert.Single(Critter(focus)));
    }

    [Fact]
    public void A_critter_still_there_after_its_take_is_reported_once()
    {
        var rig = Rig();
        rig.Game.Add(LoopRig.Frogs, 3, Swarm);
        rig.ScanFor(6000);   // the fake game never removes it
        var lines = Critter(rig);
        Assert.Equal(2, lines.Length);
        Assert.Contains("still there", lines[1]);
        Assert.Equal(1, rig.Loop.Counters.CrittersStuck);
        Assert.Contains("critters=1/1/1", rig.Loop.Counters.CountersLine());
    }

    [Fact]
    public void A_critter_taken_and_gone_writes_no_second_line()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 3, Swarm);
        rig.ScanAfter(100);
        rig.Game.Remove(frogs);
        rig.ScanFor(5000);
        Assert.Single(Critter(rig));
        Assert.Equal(0, rig.Loop.Counters.CrittersStuck);
    }

    [Fact]
    public void A_bait_swarm_template_the_table_does_not_know_is_named_once()
    {
        var rig = Rig();
        var g = Guid.NewGuid();
        rig.Game.TemplateNames[g] = "Lootpickup_Scatter_VFX_Bait_T8_NewBug_Above";
        rig.Game.Add(g, 3, Swarm);
        rig.Game.Add(g, 4, Swarm);
        var other = Guid.NewGuid();
        rig.Game.TemplateNames[other] = "Prop_Some_Rock";
        rig.Game.Add(other, 2, Swarm);
        rig.ScanFor(1000);
        var line = Assert.Single(Critter(rig));
        Assert.Contains("Lootpickup_Scatter_VFX_Bait_T8_NewBug_Above", line);
        Assert.Contains("not in the mod's table", line);
    }

    [Fact]
    public void The_lines_stop_at_the_cap_with_one_line_saying_so()
    {
        var w = new CritterWatch();
        var lines = Enumerable.Range(0, CritterWatch.MaxLines + 10).Select(i => w.Left((ulong)i, "critter: x")).Where(l => l is not null).ToList();
        Assert.Equal(CritterWatch.MaxLines + 1, lines.Count);
        Assert.Contains("left out", lines[^1]);
        w.Reset();
        Assert.NotNull(w.Left(1, "critter: x"));
    }
}
