// SPDX-License-Identifier: MIT
// Rules found missing in review: unique rewards that are left for the player, and the two boss corpses whose names
// differ from their enemies'.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class UniqueRewardTests
{
    public static TheoryData<uint, string, string> Rewards => new()
    {
        { 961402268, "Prop_Decoration_Comfort_T6_Plush_Critter", "BuildTools" },
        { 3123715137, "Vanity_TX_Farmer_Helmet", "Equipment" },
        { 1390932160, "Ammo_T1_Spell_Ice_Bolt_lvl5_Eternal", "Ammunition" },
        { 397674071, "Ammo_T2_Arrow_Magic_Ice_Eternal_lvl10", "Ammunition" },
        { 1334157650, "Ammo_T6_Spell_Fire_Ball_lvl36_Eternal", "Ammunition" },
        { 1625866381, "Ammo_T6_Spell_Heal_Beam_Channel_lvl36_Eternal", "Ammunition" },
        { 2029595221, "Ammo_T6_Spell_Ice_Bolt_lvl36_Eternal", "Ammunition" },
        { 1389638766, "Ammo_T6_Spell_Lightning_Channel_lvl36_Eternal", "Ammunition" },
        { 3833476832, "Ammo_T7_Arrow_Magic_Ice_Eternal_lvl45", "Ammunition" },
    };

    [Theory]
    [MemberData(nameof(Rewards))]
    public void Each_unique_reward_stays_in_a_container(uint id, string name, string category)
    {
        var item = Facts.I(id, name, category);
        var c = Facts.Run([], [item, Facts.I(2, "Material_T1_Stone", "Materials")]);
        Assert.Contains(id, c.QuestItems);
        var quest = QuestItems.FromIds(c.QuestItems);
        var s = new PersonalSettings();   // leaveQuestItems on by default
        var none = new IgnoreList("", ItemTable.Empty());
        Assert.Equal(SlotRule.KeepQuest, LootRules.Slot(id, s, quest, none));
        Assert.Equal(SlotRule.Take, LootRules.Slot(2, s, quest, none));
    }

    [Fact]
    public void Other_eternal_ammunition_is_not_a_quest_item()
    {
        Assert.False(ItemRules.IsQuestItem(Facts.I(1, "Ammo_T3_Spell_Fire_Ball_lvl15_Eternal", "Ammunition")));
    }
}

public class BossCorpseAliasTests
{
    [Theory]
    [InlineData("LootContainer_Enemy_Fogger_Berserker", "Enemy_Fogger_Heavy_Berserker")]
    [InlineData("LootContainer_Enemy_Fogger_HeavyMage", "Enemy_Fogger_MageHeavy_hook")]
    public void The_corpse_of_a_boss_with_a_different_name_is_a_boss_corpse(string corpseName, string enemyName)
    {
        var corpse = Facts.T(corpseName, Facts.Corpse);
        var enemy = Facts.T(enemyName, [.. Facts.Enemy, "Boss"]);
        var rule = Facts.Rule(Facts.Run([corpse, enemy]), corpse);
        Assert.NotNull(rule);
        Assert.Equal(Group.Corpse, rule!.Group);
        Assert.True((rule.Flags & TemplateFlags.Boss) != 0);

        var s = new PersonalSettings { Corpses = true, Bosses = false };
        var none = new IgnoreList("", ItemTable.Empty());
        Assert.Equal(Verdict.GroupOff, LootRules.Decide(rule, released: true, 1f, s, none, out _));
        s.Bosses = true;
        Assert.Equal(Verdict.Allowed, LootRules.Decide(rule, released: true, 1f, s, none, out _));
    }
}
