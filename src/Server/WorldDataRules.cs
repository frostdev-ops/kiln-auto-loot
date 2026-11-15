// SPDX-License-Identifier: MIT
// The stack-size and backpack rules as pure functions: no Kiln calls.
namespace KilnAutoLoot.Server;

internal static class WorldDataRules
{
    /// <summary>The 16-bit field's cap.</summary>
    public const int MaxStack = 65535;

    /// <summary>Backpack_T3_NEW, the best backpack.</summary>
    public const string BestBackpack = "Backpack_T3_NEW";
    public static readonly Guid BestBackpackGuid = new("e43e65ad-24d6-4f1e-8c76-0270ef5ab0c6");

    /// <summary>Recipes whose debug name starts with this are backpack recipes.</summary>
    public const string BackpackRecipePrefix = "Special_Backpack_";

    /// <summary>An item's max stack for the wished size. 0 (or less) = the game's own; single items
    /// (1) stay 1; a wish below the game's own never lowers it; at most 65535.</summary>
    public static int StackTarget(int original, int wish)
    {
        if (wish <= 0 || original <= 1) return original;
        var target = Math.Min(wish, MaxStack);
        return target > original ? target : original;
    }

    public static bool IsBackpackRecipe(string debugName) =>
        debugName.StartsWith(BackpackRecipePrefix, StringComparison.Ordinal);

    /// <summary>Every input stack with a count above 1 needs 1 (category inputs keep their category).</summary>
    public static int BackpackInputCount(int count) => count > 1 ? 1 : count;
}
