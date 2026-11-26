// SPDX-License-Identifier: MIT
// What the mod reads from the player's own game data (Server/GameData reads it, the JSON cache keeps it), as plain
// records: no Kiln types, so the classifiers in src/Core are fed and tested without the game.
namespace KilnAutoLoot.Core;

/// <summary>One entity template of the game.</summary>
/// <param name="Components">The short type names of its components ("Destructible", "PickupItem", ...).</param>
/// <param name="Values">The few field values the rules read, by "Component.field" ("MiningNode.resourceType" = "Wood",
/// "AreaLootOverride.exclude" = "true"). Text as the game's reflection gives it; enums by name.</param>
/// <param name="Drops">Templates this one drops as pickups (its pickup drop references).</param>
/// <param name="Yield">Item ids it gives: its pickup item and the items of its default inventory.</param>
/// <param name="YieldMax">Per item of its default inventory, the most that inventory can give (the countMax of every
/// stack naming the item, added up), before the world's Loot Amount scales it; null = none known.</param>
public sealed record TemplateFacts(Guid Guid, string Name, string Family, IReadOnlyList<string> Components,
    IReadOnlyDictionary<string, string> Values, IReadOnlyList<Guid> Drops, IReadOnlyList<uint> Yield,
    IReadOnlyDictionary<uint, int>? YieldMax = null)
{
    private HashSet<string>? _set;

    public bool Has(string component) => (_set ??= new HashSet<string>(Components, StringComparer.Ordinal)).Contains(component);

    public string? Value(string key) => Values.TryGetValue(key, out var v) ? v : null;

    public bool IsTrue(string key) => string.Equals(Value(key), "True", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One item of the game.</summary>
/// <param name="Category">The game's item category, as the game spells it ("Materials", "Consumables", ...).</param>
/// <param name="English">The English display name, "" when none is known.</param>
/// <param name="German">The German display name, "" when none is known.</param>
/// <param name="Templates">Every template GUID the item's data refers to (what it places, drops or shows).</param>
/// <param name="Values">The few field values the rules read ("isQuestItem", ...), as the reflection gives them.</param>
public sealed record ItemFacts(uint Id, string Name, Guid Guid, string Category, int MaxStack, string English, string German,
    IReadOnlyList<Guid> Templates, IReadOnlyDictionary<string, string> Values)
{
    public string? Value(string key) => Values.TryGetValue(key, out var v) ? v : null;
}

/// <summary>Everything read from one game's data.</summary>
public sealed record GameFacts(IReadOnlyList<TemplateFacts> Templates, IReadOnlyList<ItemFacts> Items);
