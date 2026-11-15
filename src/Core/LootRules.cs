// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Whether a group is allowed: switches, then the group radius, then the yield against the ignore list.</summary>
public static class LootRules
{
    /// <summary>The group a target is judged as: a released enemy (a dead one) counts as a corpse.</summary>
    public static Group EffectiveGroup(TemplateRule rule, bool released) =>
        rule.Group == Group.Enemy && released ? Group.Corpse : rule.Group;

    /// <param name="released">a container target the game would release, or a dead enemy.
    /// Pickups pass false.</param>
    /// <param name="distanceSquared">squared distance from the player; below 0 = unknown, no radius check.</param>
    /// <param name="noticeItem">the first ignored yield id when the verdict is <see cref="Verdict.YieldIgnored"/>, else 0.
    /// The caller passes it to <see cref="NoticeBook.ShouldNotify"/>.</param>
    /// <remarks>A living enemy or the boss group is <see cref="Verdict.GroupOff"/> (it is counted there); the caller
    /// checks "not released" for containers itself (<see cref="Verdict.NotReleased"/>) and the enemy-near rule.</remarks>
    public static Verdict Decide(TemplateRule? rule, bool released, float distanceSquared, PersonalSettings s, IgnoreList ignore,
        out uint noticeItem)
    {
        noticeItem = 0;
        if (rule is null) return Verdict.UnknownTemplate;   // unknown = never
        var group = EffectiveGroup(rule, released);
        bool bossCorpse = group == Group.Corpse && (rule.Flags & (TemplateFlags.Boss | TemplateFlags.Elite)) != 0;
        bool off = group switch
        {
            Group.Ground => !s.GroundLoot || ((rule.Flags & TemplateFlags.Stone) != 0 && !s.Stones),
            Group.Collectible => !s.Collectibles,
            Group.Plant => !s.Plants,
            Group.Farm => !s.Crops,
            Group.Corpse => !s.Corpses || (bossCorpse && !s.Bosses),
            Group.Chest => !s.Chests,
            Group.Boss or Group.Enemy => true,
            Group.Fish => !s.Fish,
            _ => false,
        };
        if (off) return Verdict.GroupOff;
        if (distanceSquared >= 0)   // the group radius
        {
            float r = GroupRadius(group, s);
            if (distanceSquared > r * r) return Verdict.RadiusOut;
        }
        if (rule.Yield.Count > 0)   // the yield against the ignore list
        {
            bool harvested = group is Group.Plant or Group.Farm or Group.Ground;
            uint first = 0;
            bool allIgnored = true;
            foreach (var id in rule.Yield)
            {
                if (ignore.Contains(id)) { if (first == 0) first = id; }
                else allIgnored = false;
            }
            if (first != 0 && (allIgnored || harvested)) { noticeItem = first; return Verdict.YieldIgnored; }
        }
        return Verdict.Allowed;
    }

    /// <summary>The radius a group is checked against: its own if above 0, else radius.
    /// Pass the effective group (a released enemy = <see cref="Group.Corpse"/>); fish and unknown use radius.</summary>
    public static float GroupRadius(Group group, PersonalSettings s)
    {
        float r = group switch
        {
            Group.Ground => s.GroundRadius,
            Group.Collectible => s.CollectiblesRadius,
            Group.Plant => s.PlantsRadius,
            Group.Farm => s.CropsRadius,
            Group.Corpse => s.CorpsesRadius,
            Group.Chest => s.ChestsRadius,
            _ => 0,
        };
        return r > 0 ? r : s.Radius;
    }

    /// <summary>max(radius, every group radius).</summary>
    public static float SearchRadius(PersonalSettings s)
    {
        float r = s.Radius;
        foreach (var g in new[] { s.GroundRadius, s.CollectiblesRadius, s.PlantsRadius, s.CropsRadius, s.CorpsesRadius, s.ChestsRadius })
            if (g > r) r = g;
        return r;
    }

    /// <summary>The slot filter: true = this item stays in the container.</summary>
    public static bool KeepInContainer(uint itemId, PersonalSettings s, QuestItems quest, IgnoreList ignore) =>
        Slot(itemId, s, quest, ignore) != SlotRule.Take;

    /// <summary>The slot filter with its reason, in this order: quest item first, then ignored.</summary>
    public static SlotRule Slot(uint itemId, PersonalSettings s, QuestItems quest, IgnoreList ignore)
    {
        if (s.LeaveQuestItems && quest.Contains(itemId)) return SlotRule.KeepQuest;
        if (ignore.Contains(itemId)) return SlotRule.KeepIgnored;
        return SlotRule.Take;
    }
}
