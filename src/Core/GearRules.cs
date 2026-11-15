// SPDX-License-Identifier: MIT
// Gear below the player's lowest wanted rarity stays where it lies. Gear = the game's item categories Weapons and
// Equipment (weapons, armor, rings, shields). The rarity is the one instance's (a pickup's, a container slot's), in the
// game's own numbering: 0 common, 1 uncommon, 2 rare, 3 epic, 4 legendary (the same values as GearRarity).
namespace KilnAutoLoot.Core;

public static class GearRules
{
    /// <summary>A rarity that is not known (no gear entity read, an odd value).</summary>
    public const int UnknownRarity = -1;

    public static bool IsGear(ItemRow row) => row.Category is "Weapons" or "Equipment";

    /// <summary>The item is gear whose rarity is below <paramref name="min"/>. An item missing from the table, or an
    /// unknown rarity, is never held back (nothing is left on a guess).</summary>
    public static bool BelowMin(uint item, int rarity, GearRarity min, ItemTable items)
    {
        if (min <= GearRarity.Common || rarity < 0 || rarity > (int)GearRarity.Legendary) return false;
        if (!items.TryGet(item, out var row) || !IsGear(row)) return false;
        return rarity < (int)min;
    }
}
