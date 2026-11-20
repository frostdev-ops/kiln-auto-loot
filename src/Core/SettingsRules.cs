// SPDX-License-Identifier: MIT
// The server's caps on what a player chooses: the server clamps a player's settings to ServerLimits.
namespace KilnAutoLoot.Core;

public static class SettingsRules
{
    /// <summary>A copy of <paramref name="s"/>: every field to its [Range] (a NaN goes to the low end), every radius
    /// (radius, the group radii, enemyDistance) at most <see cref="ServerLimits.MaxRadius"/> with a group radius of 0
    /// staying 0; smash, fellTrees, smashHives off unless the server allows them; language auto/en/de (else
    /// auto); a null skipList, onlyList or carryLimits becomes ""; heightAbove / heightBelow to 0-50, keepFreeSlots to
    /// 0-64; an itemFilter or minGearRarity that is no known value goes to skip / common.</summary>
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
        c.OnlyList ??= "";
        c.CarryLimits ??= "";
        c.HeightAbove = Range(c.HeightAbove, 0, 50);
        c.HeightBelow = Range(c.HeightBelow, 0, 50);
        c.KeepFreeSlots = Math.Clamp(c.KeepFreeSlots, 0, 64);
        if (!Enum.IsDefined(c.ItemFilter)) c.ItemFilter = FilterMode.Skip;
        if (!Enum.IsDefined(c.MinGearRarity)) c.MinGearRarity = GearRarity.Common;
        if (c.Language is not ("auto" or "en" or "de")) c.Language = "auto";
        // the rules as the server reads them, at most six; off for everyone when the server allows no salvage
        c.SalvageRules = limits.AllowSalvage ? KilnAutoLoot.Core.SalvageRules.Format(KilnAutoLoot.Core.SalvageRules.Parse(c.SalvageRules)) : "";
        if (!Enum.IsDefined(c.SalvageRuleAction)) c.SalvageRuleAction = SalvageAction.Mark;
        return c;
    }

    /// <summary>The skip list, the only list and the carry limits are each within <see cref="SkipListBounds"/>; a
    /// player's settings with one over are refused whole (the previous ones stay).</summary>
    public static bool ListsWithin(PersonalSettings s) =>
        SkipListBounds.Within(s.SkipList) && SkipListBounds.Within(s.OnlyList) && SkipListBounds.Within(s.CarryLimits);

    /// <summary>The most UTF-8 bytes the settings may take as JSON in one PersonalMsg (Net.Channels.MaxJson).</summary>
    public const int MaxJsonBytes = 128 * 1024;

    /// <summary>The JSON form of the settings on the network (camelCase keys): Net.SettingsJson writes and reads with it.</summary>
    internal static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>The settings, written as they cross the network, fit one PersonalMsg's JSON (<see cref="MaxJsonBytes"/>
    /// bytes of UTF-8). A player's settings that do not are refused whole on the server like lists over their bounds;
    /// the window checks before it saves or sends.</summary>
    public static bool FitsMessage(PersonalSettings s) =>
        System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(s, JsonOptions)) <= MaxJsonBytes;

    private static float Range(float v, float lo, float hi) => float.IsNaN(v) || v < lo ? lo : v > hi ? hi : v;
}
