// SPDX-License-Identifier: MIT
// "/autoloot near" (0.2.8): the owner's Vukah corpse on 0.2.8-test.1 was never looted and left no kiln.log line, and
// nothing in the log could say why. The report lists every entity within the player's reach: what it is, the lists it
// is in, its inventory, and what the next whole scan decided for it, with the reason; and why the scan passed the
// player by, if it did.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class NearReportTests
{
    private const uint Apple = 6, Flint = 7;

    private static (List<string> Log, List<string> Chat) Report(LoopRig rig, IEnumerable<EntityId>? others = null)
    {
        List<string>? log = null, chat = null;
        var report = NearReport.Around(rig.Game, rig.Player, new WorldPosition(0, 0, 0), 15, others ?? [],
            (l, c) => { log = l.ToList(); chat = c.ToList(); });
        rig.Loop.Explain(report);
        rig.ScanAfter(100);
        Assert.NotNull(log);
        return (log!, chat!);
    }

    [Fact]
    public void The_report_says_what_each_entity_in_reach_is_and_what_the_scan_decided()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.GroundLoot = false; });
        var twig = rig.Game.Add(LoopRig.Twig, 2, [], pickupItem: 1);
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv);
        corpse.Slots = [new ContainerSlot(10, Flint, 6), new ContainerSlot(11, Apple, 5)];
        rig.Game.TemplateNames[LoopRig.Vukah] = "LootContainer_Enemy_Corpse_Vukah";
        var (log, chat) = Report(rig);

        Assert.Contains("player: looked around", log[0]);
        var t = Assert.Single(log, l => l.StartsWith($"near: {twig.Id} ", StringComparison.Ordinal));
        Assert.Contains("ground", t);
        Assert.Contains("lists P", t);
        Assert.Contains("refused (GroupOff)", t);
        var c = Assert.Single(log, l => l.StartsWith($"near: {corpse.Id} ", StringComparison.Ordinal));
        Assert.Contains("LootContainer_Enemy_Corpse_Vukah", c);
        Assert.Contains("corpse", c);
        Assert.Contains("lists OI", c);
        Assert.Contains("inventory 2 stack(s): Flint x6, Apple x5", c);
        Assert.Contains("loop: ", c);
        Assert.Contains(chat, l => l.Contains("LootContainer_Enemy_Corpse_Vukah 3 m"));
    }

    [Fact]
    public void An_entity_the_table_knows_that_is_in_no_candidate_list_is_named()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var hidden = rig.Game.Add(LoopRig.Vukah, 3, []);   // no component the lists look for
        var (log, _) = Report(rig, [hidden.Id]);
        Assert.Contains("in NO candidate list", Assert.Single(log, l => l.StartsWith($"near: {hidden.Id} ", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_report_says_why_the_scan_passed_the_player_by()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        rig.Loop.SetWindowOpen(rig.Player, true);   // the F8 window, or the inventory, as the player's game reports
        rig.Game.Add(LoopRig.Twig, 2, [], pickupItem: 1);
        var (log, chat) = Report(rig);
        Assert.Contains("window or a game window is open", log[0]);
        Assert.Contains("window or a game window is open", chat[0]);
    }
}
