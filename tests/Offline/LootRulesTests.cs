// SPDX-License-Identifier: MIT
// Whether a group is allowed, the search radius, yield vs ignore, the slot filter.
using KilnAutoLoot.Core;
using static KilnAutoLoot.Offline.Fixtures;

namespace KilnAutoLoot.Offline;

public class LootRulesTests
{
    private static readonly IgnoreList NoIgnore = Ignore("");

    private static Verdict D(TemplateRule? r, PersonalSettings s, bool released = false, float d2 = 1f, IgnoreList? ig = null) =>
        LootRules.Decide(r, released, d2, s, ig ?? NoIgnore, out _);

    public static TheoryData<Group, string> Switches => new()
    {
        { Group.Ground, nameof(PersonalSettings.GroundLoot) },
        { Group.Collectible, nameof(PersonalSettings.Collectibles) },
        { Group.Plant, nameof(PersonalSettings.Plants) },
        { Group.Farm, nameof(PersonalSettings.Crops) },
        { Group.Corpse, nameof(PersonalSettings.Corpses) },
        { Group.Chest, nameof(PersonalSettings.Chests) },
        { Group.Fish, nameof(PersonalSettings.Fish) },
    };

    [Theory]
    [MemberData(nameof(Switches))]
    public void Each_group_follows_its_switch(Group g, string property)
    {
        var s = new PersonalSettings();
        var p = typeof(PersonalSettings).GetProperty(property)!;
        p.SetValue(s, true);
        Assert.Equal(Verdict.Allowed, D(Rule(g), s));
        p.SetValue(s, false);
        Assert.Equal(Verdict.GroupOff, D(Rule(g), s));
    }

    [Fact]
    public void Unknown_template_never_unknown_group_word_never_refused()
    {
        var s = new PersonalSettings();
        Assert.Equal(Verdict.UnknownTemplate, D(null, s));
        Assert.Equal(Verdict.UnknownTemplate, D(null, s, released: true));
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Unknown), s));
    }

    [Fact]
    public void Living_enemies_and_the_boss_group_are_never_looted()
    {
        var s = new PersonalSettings();
        Assert.Equal(Verdict.GroupOff, D(Rule(Group.Enemy), s));
        Assert.Equal(Verdict.GroupOff, D(Rule(Group.Boss), s));
        Assert.Equal(Verdict.GroupOff, D(Rule(Group.Boss), s, released: true));
    }

    [Fact]
    public void Released_enemy_is_a_corpse()
    {
        var s = new PersonalSettings();
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Enemy), s, released: true));
        s.Corpses = false;
        Assert.Equal(Verdict.GroupOff, D(Rule(Group.Enemy), s, released: true));
        Assert.Equal(Group.Corpse, LootRules.EffectiveGroup(Rule(Group.Enemy), true));
        Assert.Equal(Group.Enemy, LootRules.EffectiveGroup(Rule(Group.Enemy), false));
    }

    [Theory]
    [InlineData(Group.Corpse, TemplateFlags.Boss, false)]
    [InlineData(Group.Corpse, TemplateFlags.Elite, false)]
    [InlineData(Group.Enemy, TemplateFlags.Boss, true)]
    [InlineData(Group.Enemy, TemplateFlags.Elite | TemplateFlags.Boss, true)]
    public void Boss_and_elite_corpses_follow_lootBosses(Group g, TemplateFlags f, bool released)
    {
        var s = new PersonalSettings();
        Assert.Equal(Verdict.Allowed, D(Rule(g, f), s, released));
        s.Bosses = false;
        Assert.Equal(Verdict.GroupOff, D(Rule(g, f), s, released));
        Assert.Equal(Verdict.Allowed, D(Rule(g), s, released));   // a normal corpse is not affected
    }

    [Fact]
    public void Stones_are_a_sub_switch_of_ground_loot()
    {
        var s = new PersonalSettings();
        var stone = Rule(Group.Ground, TemplateFlags.Stone);
        var twig = Rule(Group.Ground);
        Assert.Equal(Verdict.Allowed, D(stone, s));
        s.Stones = false;
        Assert.Equal(Verdict.GroupOff, D(stone, s));
        Assert.Equal(Verdict.Allowed, D(twig, s));
        s.Stones = true;
        s.GroundLoot = false;
        Assert.Equal(Verdict.GroupOff, D(stone, s));    // ground off = stones off too
        Assert.Equal(Verdict.GroupOff, D(twig, s));
    }

    [Fact]
    public void Group_radius_may_exceed_the_general_radius()
    {
        var s = new PersonalSettings { Radius = 15, GroundRadius = 20 };
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Ground), s, d2: 19.9f * 19.9f));
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Ground), s, d2: 400f));             // <= r*r
        Assert.Equal(Verdict.RadiusOut, D(Rule(Group.Ground), s, d2: 400.1f));
        Assert.Equal(Verdict.RadiusOut, D(Rule(Group.Chest), s, d2: 16f * 16f));      // 0 = the general 15 m
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Chest), s, d2: 225f));
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Chest), s, d2: -1f));              // unknown distance: no check
        Assert.Equal(20f, LootRules.SearchRadius(s));
        s.ChestsRadius = 30;
        Assert.Equal(30f, LootRules.SearchRadius(s));
        Assert.Equal(15f, LootRules.SearchRadius(new PersonalSettings()));
    }

    [Fact]
    public void Group_radius_per_group_and_released_enemies_use_the_corpse_radius()
    {
        var s = new PersonalSettings { Radius = 15, GroundRadius = 1, CollectiblesRadius = 2, PlantsRadius = 3,
            CropsRadius = 4, CorpsesRadius = 5, ChestsRadius = 6 };
        Assert.Equal(1f, LootRules.GroupRadius(Group.Ground, s));
        Assert.Equal(2f, LootRules.GroupRadius(Group.Collectible, s));
        Assert.Equal(3f, LootRules.GroupRadius(Group.Plant, s));
        Assert.Equal(4f, LootRules.GroupRadius(Group.Farm, s));
        Assert.Equal(5f, LootRules.GroupRadius(Group.Corpse, s));
        Assert.Equal(6f, LootRules.GroupRadius(Group.Chest, s));
        Assert.Equal(15f, LootRules.GroupRadius(Group.Fish, s));
        Assert.Equal(15f, LootRules.GroupRadius(Group.Unknown, s));
        Assert.Equal(Verdict.Allowed, D(Rule(Group.Enemy), s, released: true, d2: 25f));
        Assert.Equal(Verdict.RadiusOut, D(Rule(Group.Enemy), s, released: true, d2: 36f));
    }

    // harvested groups refuse on any ignored yield id; the others only when all are ignored.
    [Theory]
    [InlineData(Group.Ground, true, true)]
    [InlineData(Group.Plant, true, true)]
    [InlineData(Group.Farm, true, true)]
    [InlineData(Group.Corpse, false, true)]
    [InlineData(Group.Chest, false, true)]
    [InlineData(Group.Collectible, false, true)]
    [InlineData(Group.Fish, false, true)]
    [InlineData(Group.Unknown, false, true)]
    public void Yield_against_the_ignore_list(Group g, bool someIgnoredRefuses, bool allIgnoredRefuses)
    {
        var s = new PersonalSettings { Fish = true };
        var ignore = Ignore("=Material_T1_Twigs");   // id 1
        var some = LootRules.Decide(Rule(g, TemplateFlags.None, 2, 1, 3), false, 1f, s, ignore, out var n1);
        Assert.Equal(someIgnoredRefuses ? Verdict.YieldIgnored : Verdict.Allowed, some);
        Assert.Equal(someIgnoredRefuses ? 1u : 0u, n1);
        var all = LootRules.Decide(Rule(g, TemplateFlags.None, 1), false, 1f, s, ignore, out var n2);
        Assert.Equal(allIgnoredRefuses ? Verdict.YieldIgnored : Verdict.Allowed, all);
        Assert.Equal(1u, n2);
        Assert.Equal(Verdict.Allowed, LootRules.Decide(Rule(g, TemplateFlags.None, 2, 3), false, 1f, s, ignore, out var n3));
        Assert.Equal(0u, n3);
        Assert.Equal(Verdict.Allowed, LootRules.Decide(Rule(g), false, 1f, s, ignore, out _));   // no yield list
    }

    [Fact]
    public void Notice_item_is_the_first_ignored_yield_id()
    {
        var ignore = Ignore("=Material_T1_Twigs, =Material_T1_Stone");
        LootRules.Decide(Rule(Group.Plant, TemplateFlags.None, 3, 2, 1), false, 1f, new PersonalSettings(), ignore, out var n);
        Assert.Equal(2u, n);
    }

    [Fact]
    public void Order_switch_before_radius_before_yield()
    {
        var s = new PersonalSettings { Plants = false };
        var ignore = Ignore("twig");
        var r = Rule(Group.Plant, TemplateFlags.None, 1);
        Assert.Equal(Verdict.GroupOff, LootRules.Decide(r, false, 1e6f, s, ignore, out var n));
        Assert.Equal(0u, n);
        s.Plants = true;
        Assert.Equal(Verdict.RadiusOut, LootRules.Decide(r, false, 1e6f, s, ignore, out n));
        Assert.Equal(0u, n);
        Assert.Equal(Verdict.YieldIgnored, LootRules.Decide(r, false, 1f, s, ignore, out n));
    }

    [Fact]
    public void Slot_filter_quest_first_then_ignored()
    {
        var s = new PersonalSettings();
        var quest = Quest(4, 1);
        var ignore = Ignore("=Material_T1_Twigs, =Quest_Key");
        Assert.Equal(SlotRule.KeepQuest, LootRules.Slot(4, s, quest, ignore));   // quest and ignored: quest wins
        Assert.Equal(SlotRule.KeepQuest, LootRules.Slot(1, s, quest, ignore));
        Assert.Equal(SlotRule.Take, LootRules.Slot(2, s, quest, ignore));
        s.LeaveQuestItems = false;
        Assert.Equal(SlotRule.KeepIgnored, LootRules.Slot(4, s, quest, ignore));
        Assert.Equal(SlotRule.Take, LootRules.Slot(2, s, quest, Ignore("")));
        Assert.True(LootRules.KeepInContainer(1, s, quest, ignore));
        Assert.False(LootRules.KeepInContainer(2, s, quest, ignore));
    }
}
