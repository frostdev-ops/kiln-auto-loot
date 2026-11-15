// SPDX-License-Identifier: MIT
// Items the quest-item rule (ItemRules.IsQuestItem) gets wrong by its name alone, by internal item name. Keep it short:
// each entry says why it is here.
namespace KilnAutoLoot.Core;

public static class QuestItemExceptions
{
    /// <summary>Quest items although no rule catches them.</summary>
    public static readonly HashSet<string> Always = new(StringComparer.Ordinal)
    {
        // the coffee maker's two parts: workshop parts like the "Prop_Component_" ones (rare in the data), but named
        // as a production station
        "Prop_Production_T6_coffeemaker_brewer",
        "Prop_Production_T6_coffeemaker_roaster",
        // a tool the game names for the prospector villager: it belongs to that villager's task, not to the player's kit
        "Tool_T6_GoldSieve_Prospector_Wetlands",
        // one-of-a-kind rewards that wait in a quest's or a dungeon's own chest; the player is meant to find and take
        // them there, and some quests look for them in the chest. A plush toy for the base, a farmer's hat:
        "Prop_Decoration_Comfort_T6_Plush_Critter",
        "Vanity_TX_Farmer_Helmet",
        // eternal spells and arrows (they never run out) placed as fixed rewards. Most eternal ammunition is loot like
        // any other; these seven are the ones that sit in such reward chests, so a name rule would catch too many:
        "Ammo_T1_Spell_Ice_Bolt_lvl5_Eternal",
        "Ammo_T2_Arrow_Magic_Ice_Eternal_lvl10",
        "Ammo_T6_Spell_Fire_Ball_lvl36_Eternal",
        "Ammo_T6_Spell_Heal_Beam_Channel_lvl36_Eternal",
        "Ammo_T6_Spell_Ice_Bolt_lvl36_Eternal",
        "Ammo_T6_Spell_Lightning_Channel_lvl36_Eternal",
        "Ammo_T7_Arrow_Magic_Ice_Eternal_lvl45",
    };

    /// <summary>Not quest items although a rule would say so.</summary>
    public static readonly HashSet<string> Never = new(StringComparer.Ordinal)
    {
    };
}
