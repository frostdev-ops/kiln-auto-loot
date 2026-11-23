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

        var high = Rig(s => s.GroundRadius = 30);   // the search reaches 30 m, critters the 15 m main radius
        high.Game.Add(LoopRig.Frogs, 0, Swarm, y: 20);   // 20 m above: beyond the main radius in 3D
        high.ScanFor(1000);
        var highLine = Assert.Single(Critter(high));
        Assert.Contains("beyond the main radius (15 m)", highLine);
        Assert.DoesNotContain("height window", highLine);

        var far = Rig(s => { s.Radius = 5; s.GroundRadius = 20; });   // the search reaches 20 m, critters only 5
        far.Game.Add(LoopRig.Frogs, 8, Swarm);
        far.ScanFor(1000);
        Assert.Contains("beyond the main radius (5 m)", Assert.Single(Critter(far)));

        var focus = Rig(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Material_T1_Twigs"; });
        focus.Game.Add(LoopRig.Frogs, 3, Swarm);
        focus.ScanFor(1000);
        Assert.Contains("only list", Assert.Single(Critter(focus)));
    }

    // 0.2.5: Tyrion67 stood right below two maggot swarms (radius 15 m) and they stayed: critters hover above the
    // ground, so the height window (4 m by default) left them. Critters now ignore the height window and count within
    // the main radius in 3D (a sphere).
    [Fact]
    public void A_critter_6_m_above_is_taken_despite_the_4_m_height_window()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 0, Swarm, y: 6);
        rig.ScanFor(1000);
        Assert.Contains(rig.Game.Loots, l => l.Host == frogs.Id);
        var line = Assert.Single(Critter(rig));
        Assert.Contains("loot sent", line);
        Assert.Equal(0, rig.Loop.Counters.HeightOut);
    }

    [Fact]
    public void A_critter_6_m_below_is_taken_too()
    {
        var rig = Rig();
        var frogs = rig.Game.Add(LoopRig.Frogs, 0, Swarm, y: -6);
        rig.ScanFor(1000);
        Assert.Contains(rig.Game.Loots, l => l.Host == frogs.Id);
    }

    [Fact]
    public void A_critter_20_m_above_with_a_15_m_radius_is_left()
    {
        var rig = Rig(s => s.Radius = 15);
        var frogs = rig.Game.Add(LoopRig.Frogs, 0, Swarm, y: 20);
        rig.ScanFor(1000);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == frogs.Id);
    }

    [Fact]
    public void A_critter_inside_the_cylinder_but_outside_the_sphere_is_left()
    {
        // 12 m out and 12 m up: horizontally within 15 m, but 17 m away in 3D
        var rig = Rig(s => { s.Radius = 15; s.GroundRadius = 30; s.HeightAbove = 0; });
        var frogs = rig.Game.Add(LoopRig.Frogs, 12, Swarm, y: 12);
        rig.ScanFor(1000);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == frogs.Id);
        Assert.Contains("beyond the main radius (15 m)", Assert.Single(Critter(rig)));
    }

    [Fact]
    public void A_non_critter_6_m_above_is_still_left_by_the_height_window()
    {
        var rig = Rig(s => s.HeightAbove = 4);
        rig.Game.SightOn = false;   // the height window: the fallback where line of sight cannot decide (0.2.8)
        var twig = rig.Game.Add(LoopRig.Twig, 0, [LootLoop.PickupComponent], pickupItem: 1, y: 6);
        rig.ScanFor(1000);
        Assert.DoesNotContain(rig.Game.Loots, l => l.Host == twig.Id);
        Assert.True(rig.Loop.Counters.HeightOut > 0);
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
    public void An_unknown_bait_template_line_names_its_guid_and_that_the_read_game_data_had_no_such_template()
    {
        var rig = Rig();
        var g = Guid.NewGuid();
        rig.Game.TemplateNames[g] = "Lootpickup_Scatter_VFX_Bait_T7_Maggots_Above";
        rig.Game.Add(g, 3, Swarm);
        rig.ScanFor(1000);
        var line = Assert.Single(Critter(rig));
        Assert.Contains(g.ToString(), line);
        Assert.Contains("the game data the mod read has no bait template with this GUID", line);
    }

    [Fact]
    public void An_unknown_bait_template_line_says_what_the_game_data_showed_of_it()
    {
        var rig = Rig();
        var g = Guid.NewGuid();
        rig.Game.TemplateNames[g] = "Lootpickup_Scatter_VFX_Bait_T7_Maggots_Above";
        rig.Data.Templates.Fill(rig.Data.Templates.All.ToList(), [],
            new Dictionary<Guid, string> { [g] = "no yield (its pickup names no item and its stock gave none); not taken" });
        rig.Game.Add(g, 3, Swarm);
        rig.ScanFor(1000);
        var line = Assert.Single(Critter(rig));
        Assert.Contains("not as fishing bait: no yield (its pickup names no item and its stock gave none); not taken", line);
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
