// SPDX-License-Identifier: MIT
// The height window around a player: a target counts only when it is at most heightAbove meters above the player and
// at most heightBelow meters below (0 = no limit on that side). Keeps a cave's loot in the cave and the tower's on top.
namespace KilnAutoLoot.Core;

public static class HeightWindow
{
    /// <param name="dy">target height minus player height, in meters.</param>
    public static bool Allows(double dy, float above, float below)
    {
        if (double.IsNaN(dy)) return true;
        if (above > 0 && dy > above) return false;
        if (below > 0 && dy < -below) return false;
        return true;
    }

    public static bool Allows(double dy, PersonalSettings s) => Allows(dy, s.HeightAbove, s.HeightBelow);

    /// <summary>Whether the height window holds a group. Critters (fishing bait swarms) hover or sit above the ground, so
    /// the window would leave a swarm right above the player: they count within the main radius in 3D instead (a sphere:
    /// the loop's distance is 3D), 0.2.5.</summary>
    public static bool AppliesTo(Group group) => group != Group.Critter;
}
