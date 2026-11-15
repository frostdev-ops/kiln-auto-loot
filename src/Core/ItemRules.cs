// SPDX-License-Identifier: MIT
// What the mod makes of the game's items, as pure rules over ItemFacts: the rows of the item table (names, menu
// group, whether the window offers the item), and which items are quest items (left in containers with
// leaveQuestItems on).
namespace KilnAutoLoot.Core;

public static class ItemRules
{
    /// <summary>Game categories whose items never come out of the world into a bag: recipes you learn, looks you
    /// unlock, the currency counter. The window does not offer them and they form no menu group.</summary>
    private static readonly HashSet<string> NotLooseCategories = new(StringComparer.Ordinal)
    {
        "Blueprints", "Customization", "Currency",
    };

    /// <summary>Every menu group an item can have, in the window's order: the game's item categories whose items lie in the
    /// world or in containers (the keys of Texts.ItemGroups).</summary>
    public static readonly IReadOnlyList<string> MenuGroups =
    [
        "Materials", "Consumables", "AnimalFood", "Ammunition", "Weapons", "Equipment", "Tools", "Instrument",
        "WeaponGem", "Collectibles", "ColorPalette", "BuildTools",
    ];

    /// <summary>The menu group of an item: the game's own item category, as the game spells it. Fishing rods are so few
    /// (five) that they go with the other tools.</summary>
    public static string MenuGroupOf(ItemFacts item) => item.Category switch
    {
        "FishingRod" => "Tools",
        var c => c,
    };

    /// <summary>Whether the window offers the item: it has a name a player can find it by, and it is something that lies in
    /// the world or in a container. Building pieces (BuildTools) are placed, not found, except the ones a template
    /// gives (paintings and the like lie in ruins).</summary>
    public static bool IsLoose(ItemFacts item, ISet<uint> givenByTemplates)
    {
        if (item.Id == 0 || item.Name.Length == 0 || NotLooseCategories.Contains(item.Category)) return false;
        if (item.Category == "BuildTools") return givenByTemplates.Contains(item.Id);
        return true;
    }

    public static ItemRow RowOf(ItemFacts item, ISet<uint> givenByTemplates) => new(
        Id: item.Id,
        Internal: item.Name,
        English: item.English,
        German: item.German,
        Lootable: IsLoose(item, givenByTemplates),
        Category: item.Category,
        MenuGroup: MenuGroupOf(item),
        Guid: item.Guid,
        OriginalMaxStack: item.MaxStack);

    /// <summary>A quest item stays in a container for the player to take by hand: things that tell or move the story on,
    /// or that exist once and count for something:
    ///   - lore texts and map pieces (reading them is the point, and some open map areas);
    ///   - instruments (each is a one-off reward);
    ///   - pieces of a collection that is put together somewhere (statue, tapestry, relief, world map pieces);
    ///   - parts for the villagers' workshops and stations, and the items villagers ask for ("Prop_Component_",
    ///     "_NPC_" in the name);
    ///   - the short list in <see cref="QuestItemExceptions"/>.</summary>
    public static bool IsQuestItem(ItemFacts item)
    {
        if (QuestItemExceptions.Never.Contains(item.Name)) return false;
        if (QuestItemExceptions.Always.Contains(item.Name)) return true;
        var n = item.Name;
        if (item.Category == "Instrument") return true;
        if (Has(n, "LoreText") || Has(n, "mapPiece") || Has(n, "treasureMap")) return true;
        if (n.StartsWith("Collection_", StringComparison.OrdinalIgnoreCase) && (Has(n, "_Piece") || Has(n, "Worldmap"))) return true;
        if (n.StartsWith("Prop_Component_", StringComparison.OrdinalIgnoreCase)) return true;
        if (Has(n, "_NPC_") || n.StartsWith("Prop_NPC_", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);
}
