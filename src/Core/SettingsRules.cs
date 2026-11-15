// SPDX-License-Identifier: MIT
// The server's caps on what a player chooses: the server clamps a player's settings to ServerLimits.
namespace KilnAutoLoot.Core;

public static class SettingsRules
{
    /// <summary>A copy of <paramref name="s"/>: every field to its [Range] (a NaN goes to the low end), every radius
    /// (radius, the group radii, enemyDistance) at most <see cref="ServerLimits.MaxRadius"/> with a group radius of 0
    /// staying 0; smash, fellTrees, smashHives off unless the server allows them; language auto/en/de (else
    /// auto); a null skipList becomes "".</summary>
    public static PersonalSettings Clamp(PersonalSettings s, ServerLimits limits)
    {
        var c = s.Clone();
        float max = Range(limits.MaxRadius, 1, 50);
        c.Radius = Math.Min(Range(c.Radius, 1, 50), max);
        c.GroundRadius = Math.Min(Range(c.GroundRadius, 0, 50), max);
        c.CollectiblesRadius = Math.Min(Range(c.CollectiblesRadius, 0, 50), max);
        c.PlantsRadius = Math.Min(Range(c.PlantsRadius, 0, 50), max);
        c.CropsRadius = Math.Min(Range(c.CropsRadius, 0, 50), max);
        c.CorpsesRadius = Math.Min(Range(c.CorpsesRadius, 0, 50), max);
        c.ChestsRadius = Math.Min(Range(c.ChestsRadius, 0, 50), max);
        c.EnemyDistance = Math.Min(Range(c.EnemyDistance, 1, 50), max);
        c.OpenDelay = Range(c.OpenDelay, 0, 5);
        if (!limits.AllowSmash) c.Smash = false;
        if (!limits.AllowFelling) c.FellTrees = false;
        if (!limits.AllowHives) c.SmashHives = false;
        c.SkipList ??= "";
        if (c.Language is not ("auto" or "en" or "de")) c.Language = "auto";
        return c;
    }

    private static float Range(float v, float lo, float hi) => float.IsNaN(v) || v < lo ? lo : v > hi ? hi : v;
}
