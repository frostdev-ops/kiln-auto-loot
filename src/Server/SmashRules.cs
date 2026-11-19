// SPDX-License-Identifier: MIT
// Which tagged target one player smashes in one step, as pure rules: no Kiln calls.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

/// <summary>One player's smash switches after the server's limits.</summary>
internal readonly record struct SmashSwitches(bool On, float Radius, bool Buildables, bool Stones, bool Trees, bool Hives)
{
    /// <summary>The player's settings capped by the server: auto loot off (looting, the F9 switch) turns smashing off as a
    /// whole, as it stops the pickups; allowSmash off turns smashing off as a whole (trees and
    /// hives included); allowFelling / allowHives turn their own switch off; the radius is at most maxRadius. Stones count
    /// only with groundLoot on, as for the pickups: with "things on the ground" off a boulder's stone would only be left
    /// lying (seen in a hosted game: boulders kept breaking with ground loot off).</summary>
    public static SmashSwitches Of(PersonalSettings s, ServerLimits limits) => new(
        On: s.Looting && s.Smash && limits.AllowSmash,
        Radius: Math.Min(s.Radius, limits.MaxRadius),
        Buildables: s.SmashBuildables,
        Stones: s.Stones && s.GroundLoot,
        Trees: s.FellTrees && limits.AllowFelling,
        Hives: s.SmashHives && limits.AllowHives);

    /// <summary>Smashing off or no radius: nothing is found at all.</summary>
    public bool Off => !On || Radius <= 0;
}

internal static class SmashRules
{
    /// <summary>At most this many hits per player per step (the engine applies at most 4 impacts per target per step);
    /// the nearest targets go first.</summary>
    public const int MaxTargetsPerStep = 4;

    /// <summary>Whether a target of <paramref name="kind"/> is smashed. The caller has already
    /// checked the save zone (nothing at all there) and the radius. <paramref name="nearBuildZone"/> is asked only
    /// for the kinds that need it.</summary>
    public static bool Wants(SmashKind kind, in SmashSwitches sw, Func<bool> nearBuildZone)
    {
        if (sw.Off) return false;
        return kind switch
        {
            // SmashTag: smash
            SmashKind.Smash => true,
            // SharedTag: only with smashBuildables and not near a build zone
            SmashKind.Shared => sw.Buildables && !nearBuildZone(),
            // StoneTag: boulders are skipped while stones is off; with it on they are smashed as their SmashTag /
            // SharedTag says
            SmashKind.Stone => sw.Stones,
            SmashKind.StoneShared => sw.Stones && sw.Buildables && !nearBuildZone(),
            // TreeTag (trees count only with fellTrees): smashed like a shared target
            SmashKind.Tree => sw.Trees && !nearBuildZone(),
            // smashHives -> SmashTag (SharedTag if buildable); only fellTrees -> TreeTag
            SmashKind.Beehive => sw.Hives || (sw.Trees && !nearBuildZone()),
            SmashKind.BeehiveShared => sw.Hives
                ? sw.Buildables && !nearBuildZone()
                : sw.Trees && !nearBuildZone(),
            _ => false,
        };
    }
}
