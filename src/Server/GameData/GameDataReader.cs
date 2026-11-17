// SPDX-License-Identifier: MIT
// Reads the facts the rules need from the running game's own data: every keen::ecs::TemplateResource and every
// keen::ItemInfo, through ReflectedResources (kiln.assets + kiln.types). Each resource is copied out once and walked
// with BlobView. Works wherever kiln.assets serves resources: a dedicated server, a hosting game, a player's game.
// Item names: the player's game has the game's own texts (TextsReader); a dedicated server has none, so there the
// English names come from Kiln's item catalog (Items.All, when kiln.items runs) and German names stay empty.
using System.Diagnostics;
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

/// <summary>How a read went, for the log.</summary>
internal sealed record ReadReport(int Templates, int Items, double TemplateMs, double ItemMs);

internal static class GameDataReader
{
    public const string TemplateType = "keen::ecs::TemplateResource";
    public const string ItemType = "keen::ItemInfo";

    /// <summary>The components whose field values are kept, and which of their fields ("" = every plain field, up to a
    /// few dozen). Everything else of a template is kept only as its component's name.</summary>
    private static readonly Dictionary<string, string[]> KeptFields = new(StringComparer.Ordinal)
    {
        ["AreaLootOverride"] = [""],
        ["MiningNode"] = [""],
        ["PickupItem"] = [""],
        ["DefaultInventory"] = [""],
        ["Faction"] = [""],
        ["PlantGrowth"] = [""],
        ["Growable"] = [""],
        ["GrowthStage"] = [""],
        ["Plant"] = [""],
        ["Wildlife"] = [""],
        ["RandomLoot"] = [""],
        ["Boss"] = [""],
        ["Destructible"] = [""],
        ["ResourceNodePickupDrops"] = [""],
        ["PickupLootSlot"] = [""],
        ["LootContainer"] = [""],
        ["InteractionOfferRuntime"] = [""],
    };

    /// <summary>The fields of a template that name other templates it drops as pickups (component.field path, relative).</summary>
    private static readonly (string Component, string Path)[] DropFields =
    [
        ("ResourceNodePickupDrops", "pickupItemReference"),
        ("PickupLootSlot", "genericPickupItem"),
    ];

    private const int MaxValuesPerComponent = 48;

    public static bool Available => ReflectedResources.Available && GameTypes.Find(TemplateType) is not null && GameTypes.Find(ItemType) is not null;

    /// <summary>A cheap fingerprint of the game data: how many templates and items there are and an FNV hash over their
    /// GUIDs (one list call each, no resource is read).</summary>
    public static string Fingerprint()
    {
        var h = 0xcbf29ce484222325UL;
        int n = 0;
        foreach (var type in new[] { TemplateType, ItemType })
            foreach (var r in ReflectedResources.All(type))
            {
                n++;
                foreach (var b in r.Guid.ToByteArray()) h = (h ^ b) * 0x100000001b3UL;
            }
        return $"{n}-{h:x16}";
    }

    /// <summary>The templates and items, without names (ItemNames adds them on the game's thread). Any thread:
    /// kiln.assets' resource reads and kiln.types answer on any thread.</summary>
    public static GameFacts Read(out ReadReport report)
    {
        var sw = Stopwatch.StartNew();
        var templates = ReadTemplates();
        var tms = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var items = ReadItems(templates.Select(t => t.Guid).ToHashSet());
        templates = Yields.Add(templates, items);
        report = new ReadReport(templates.Count, items.Count, tms, sw.Elapsed.TotalMilliseconds);
        return new GameFacts(templates, items);
    }

    private static List<TemplateFacts> ReadTemplates()
    {
        var type = GameTypes.Find(TemplateType)!.Resolved;
        var list = new List<TemplateFacts>(13_000);
        foreach (var r in ReflectedResources.All(type))
        {
            if (r.Part != 0) continue;
            byte[] data;
            try { data = r.Bytes; }
            catch (KeyNotFoundException) { continue; }
            try { list.Add(Template(r.Guid, new BlobView(data), type)); }
            catch (InvalidDataException) { /* a damaged resource: left out, so never taken */ }
        }
        return list;
    }

    private static TemplateFacts Template(Guid guid, BlobView v, GameType type)
    {
        var root = new BlobSpot(type, 0);
        var name = v.Value(v.Find(root, "name")) as string ?? "";
        var family = v.Value(v.Find(root, "family"))?.ToString() ?? "";
        var components = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var drops = new List<Guid>();
        var yield = new List<uint>();
        if (v.Find(root, "components") is { } list)
            foreach (var e in v.Elements(list))
            {
                if (v.Enter(e) is not { } c) continue;
                var cname = c.Type.Name;
                components.Add(cname);
                if (KeptFields.ContainsKey(cname) && !values.ContainsKey(cname + "."))
                {
                    values[cname + "."] = "";   // marks the first component of the type as read
                    KeepLeaves(v, c, cname, values);
                }
                foreach (var (dc, path) in DropFields)
                    if (dc == cname && v.Value(v.Find(c, path)) is Guid g && g != Guid.Empty) drops.Add(g);
                if (cname == "Upgrade" && v.Find(c, "levels") is { } levels)
                    // the growth stages of a crop, in order: the template each level turns the plant into
                    values["Upgrade.stages"] = string.Join(",", v.Elements(levels)
                        .Select(l => v.Value(v.Find(l, "upgradeEntity"))).OfType<Guid>().Where(g => g != Guid.Empty));
                if (cname == "DefaultInventory" && v.Find(c, "defaultInventory") is { } inv && inv.Type.Inner is { } invType)
                    values["DefaultInventory.type"] = invType.QualifiedName;
            }
        foreach (var key in values.Keys.Where(k => k.EndsWith('.')).ToList()) values.Remove(key);
        return new TemplateFacts(guid, name, family, components, values, drops, yield);
    }

    private static void KeepLeaves(BlobView v, BlobSpot c, string cname, Dictionary<string, string> values)
    {
        var lines = new List<string>();
        v.Describe(c, cname, lines, maxArray: 16, maxLines: MaxValuesPerComponent);
        foreach (var line in lines)
        {
            var eq = line.IndexOf(" = ", StringComparison.Ordinal);
            if (eq <= 0) continue;
            var value = line[(eq + 3)..];
            if (value.StartsWith('<')) continue;   // variants, empty optionals: no value of their own
            values.TryAdd(line[..eq], value);
        }
    }

    private static List<ItemFacts> ReadItems(HashSet<Guid> templateGuids)
    {
        var type = GameTypes.Find(ItemType)!.Resolved;
        var list = new List<ItemFacts>(4_500);
        foreach (var r in ReflectedResources.All(type))
        {
            if (r.Part != 0) continue;
            byte[] data;
            try { data = r.Bytes; }
            catch (KeyNotFoundException) { continue; }
            try
            {
                var v = new BlobView(data);
                var root = new BlobSpot(type, 0);
                var id = v.Value(v.Find(root, "itemId")) is { } i ? Convert.ToUInt32(i) : 0u;
                var name = v.Value(v.Find(root, "debugName")) as string ?? "";
                var category = v.Value(v.Find(root, "category"))?.ToString() ?? "";
                var stack = v.Value(v.Find(root, "maxStackSize")) is { } m ? Convert.ToInt32(m) : 0;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (v.Value(v.Find(root, "name")) is Guid tag && tag != Guid.Empty) values["nameTag"] = tag.ToString();
                foreach (var f in new[] { "flags", "rarity", "ammunitionType" })
                    if (v.Value(v.Find(root, f)) is { } fv) values[f] = fv.ToString() ?? "";
                var refs = new HashSet<Guid>();
                CollectGuids(v, root, refs, templateGuids, 0);
                list.Add(new ItemFacts(id, name, r.Guid, category, stack, "", "", refs.ToList(), values));
            }
            catch (InvalidDataException) { }
        }
        return list;
    }

    /// <summary>Every template GUID anywhere in an item's data: what it places, drops or shows.</summary>
    private static void CollectGuids(BlobView v, BlobSpot spot, HashSet<Guid> found, HashSet<Guid> templates, int depth)
    {
        if (depth > 12) return;
        var t = spot.Type.Resolved;
        switch (t.Kind)
        {
            case GameTypeKind.Guid or GameTypeKind.ObjectReference when t.Size == 16:
                if (v.Value(spot) is Guid g && templates.Contains(g)) found.Add(g);
                break;
            case GameTypeKind.Struct:
                foreach (var f in t.AllFields) CollectGuids(v, new BlobSpot(f.Type.Resolved, spot.Offset + f.Offset), found, templates, depth + 1);
                break;
            case GameTypeKind.StaticArray or GameTypeKind.BlobArray:
                foreach (var e in v.Elements(spot).Take(256)) CollectGuids(v, e, found, templates, depth + 1);
                break;
            case GameTypeKind.BlobOptional or GameTypeKind.BlobVariant:
                if (v.Enter(spot) is { } inner) CollectGuids(v, inner, found, templates, depth + 1);
                break;
        }
    }
}
