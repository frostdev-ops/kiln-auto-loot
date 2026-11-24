// SPDX-License-Identifier: MIT
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class VukahRangerTests
{
    // The relevant facts from the game template the running server actually reported on dead Rangers.
    private static readonly TemplateFacts Ranger = Facts.T("Base_Enemy_Vukah_Ranger",
        [.. Facts.Enemy, "RandomLoot", "RandomLootSlotCount"], new() { ["Faction.faction"] = "Vukah" },
        guid: Guid.Parse("6b71e703-ec29-428c-aa0e-0f050fe187f1"));

    [Fact]
    public void The_real_ranger_template_is_an_ordinary_enemy_with_no_fixed_yield()
    {
        var rule = Assert.Single(Facts.Run([Ranger]).Templates);
        Assert.Equal(Ranger.Guid, rule.Guid);
        Assert.Equal(Group.Enemy, rule.Group);
        Assert.Equal(TemplateFlags.None, rule.Flags);
        Assert.Empty(rule.Yield);
    }

    [Fact]
    public void Other_base_templates_and_rangers_without_the_hostile_actor_facts_stay_excluded()
    {
        var others = new[]
        {
            Ranger with { Name = "Base_Enemy_Vukah_Melee" },
            Ranger with { Name = "Base_Enemy_Vukah_Ranger_Test" },
            Facts.T(Ranger.Name, Ranger.Components.Where(c => c != "Actor").ToArray(), new() { ["Faction.faction"] = "Vukah" }),
            Facts.T(Ranger.Name, Ranger.Components.Where(c => c != "RandomLoot").ToArray(), new() { ["Faction.faction"] = "Vukah" }),
            Ranger with { Values = new Dictionary<string, string> { ["Faction.faction"] = "PlayerAlly" } },
        };
        foreach (var t in others) Assert.Empty(Facts.Run([t]).Templates);
    }

    [Fact]
    public void Only_a_dead_visible_ranger_reaches_slot_looting_and_skipped_stacks_stay()
    {
        var rig = new LoopRig(s =>
        {
            s.VisibleOnly = true;
            s.OpenDelay = 0;
            s.SkipList = "=Material_T1_Twigs";
        });
        var classification = Facts.Run([Ranger]);
        rig.Data.Templates.Fill(classification.Templates, classification.WorldLootOnly, classification.BaitNotes);
        var corpse = rig.Game.Add(Ranger.Guid, 3, LoopRig.OfferInv);
        corpse.Dead = false;
        corpse.Offer = new(true, false, 5);
        corpse.Slots = [new ContainerSlot(10, 7, 3), new ContainerSlot(11, 1, 4)];
        rig.ScanAfter(100);
        Assert.Equal(0, rig.Game.SightCalls);
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Moves);

        corpse.Dead = true;
        corpse.Sight = Sight.Hidden;
        rig.ScanAfter(100);
        Assert.Equal(3, rig.Game.SightCalls); // blocked primary and both body samples
        Assert.Empty(rig.Game.Interacts);
        Assert.Empty(rig.Game.Moves);

        corpse.Sight = Sight.Visible;
        rig.ScanAfter(LootLoop.SightCacheMs + 100);
        Assert.Single(rig.Game.Interacts);
        corpse.Offer = new(true, false, 6);
        rig.ScanAfter(100);
        Assert.Equal(10ul, Assert.Single(rig.Game.Moves).Slot);
        Assert.Empty(rig.Game.Loots);
        Assert.Contains(rig.KeyLines, l => l.Contains("left Twigs x4 (on the skip list)"));
    }
}
