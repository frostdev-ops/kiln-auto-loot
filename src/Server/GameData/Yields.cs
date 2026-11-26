// SPDX-License-Identifier: MIT
// What each template gives, as item ids: the item of its pickup, and the items of its default inventory (the game's
// stock resource a bush, crop, heap or swarm is filled with), with the most of each such item the stock can give: the
// countMax of every stack (ItemStackInfoResource) naming it, added up, before the world's Loot Amount scales the counts
// (docs/re/content.md: applyDefaultInventory rolls each stack's count in [countMin, countMax], times Loot Amount). A
// stock resource is read once and shared by every template that names it.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

internal static class Yields
{
    public static List<TemplateFacts> Add(List<TemplateFacts> templates, List<ItemFacts> items)
    {
        var byGuid = new Dictionary<Guid, uint>();
        var ids = new HashSet<uint>();
        foreach (var i in items)
        {
            byGuid.TryAdd(i.Guid, i.Id);
            ids.Add(i.Id);
        }
        var stocks = new Dictionary<Guid, (List<uint> Items, Dictionary<uint, int> Most)>();
        var result = new List<TemplateFacts>(templates.Count);
        foreach (var t in templates)
        {
            var yield = new List<uint>();
            Dictionary<uint, int>? most = null;
            // a pickup names its item by reference (the id field is often 0 in the data)
            if (t.Value("PickupItem.item") is { } r && Guid.TryParse(r, out var g) && byGuid.TryGetValue(g, out var pid)) yield.Add(pid);
            else if (t.Value("PickupItem.itemId.value") is { } v && uint.TryParse(v, out var vid) && ids.Contains(vid)) yield.Add(vid);
            if (t.Value("DefaultInventory.defaultInventory") is { } s && Guid.TryParse(s, out var stock) && stock != Guid.Empty &&
                t.Value("DefaultInventory.type") is { } type)
            {
                if (!stocks.TryGetValue(stock, out var inside)) stocks[stock] = inside = ReadStock(stock, type, byGuid, ids);
                foreach (var id in inside.Items) if (!yield.Contains(id)) yield.Add(id);
                if (inside.Most.Count > 0) most = inside.Most;
            }
            result.Add(yield.Count == 0 ? t : t with { Yield = yield, YieldMax = most });
        }
        return result;
    }

    /// <summary>The resource type of the stocks: the reference names a one-byte tag type, the data sits in a resource of
    /// this type under the same GUID (lab, game build 26430253: 420 of them).</summary>
    private const string StockType = "keen::ecs::DefaultInventoryResource";

    /// <summary>The largest count one stock may say for an item (the field is 16 or 32 bits; Kiln's drop rules allow up to
    /// 100000): a sum is capped here, which only makes a bound larger than any harvest.</summary>
    private const int MostCap = 1_000_000;

    /// <summary>Every item a stock resource names, by reference or by id, in its order, and the most of each the stock can
    /// give (the countMax of every stack naming it, added up; an item named outside a stack has none).</summary>
    private static (List<uint> Items, Dictionary<uint, int> Most) ReadStock(Guid stock, string typeName, Dictionary<Guid, uint> byGuid,
        HashSet<uint> ids)
    {
        var found = new List<uint>();
        var most = new Dictionary<uint, int>();
        try
        {
            ReflectedResource? res = null;
            foreach (var name in new[] { StockType, typeName })
                if (GameTypes.Find(name) is { } t && t.Resolved.Kind == GameTypeKind.Struct && ReflectedResources.Get(stock, t) is { } r)
                {
                    res = r;
                    break;
                }
            if (res is null) return (found, most);
            var type = res.Type;
            var v = new BlobView(res.Bytes);
            Walk(v, new BlobSpot(type.Resolved, 0), 0);
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or ArgumentException) { }
        // an item a stack names whose count could not be read has no bound (the rules then count it as unknown)
        foreach (var id in most.Where(x => x.Value <= 0).Select(x => x.Key).ToList()) most.Remove(id);
        return (found, most);

        // an ItemStackInfoResource (item, itemRef, countMin, countMax, ...): its item and countMax
        void Stack(BlobView v, BlobSpot spot, GameType t)
        {
            if (t.Field("countMax") is not { } cm) return;
            // the game resolves a stack's item by its id (the reference is never read); the reference when the id is not known
            uint id = 0;
            var raw = t.Field("item") is { } fi ? v.Value(new BlobSpot(fi.Type.Resolved, spot.Offset + fi.Offset)) : null;
            if (raw is ulong u && u <= uint.MaxValue && ids.Contains((uint)u)) id = (uint)u;
            else if (raw is long l && l is >= 0 and <= uint.MaxValue && ids.Contains((uint)l)) id = (uint)l;
            else if (t.Field("itemRef") is { } fr && v.Value(new BlobSpot(fr.Type.Resolved, spot.Offset + fr.Offset)) is Guid g &&
                     byGuid.TryGetValue(g, out var byRef))
                id = byRef;
            if (id == 0) return;
            var count = v.Value(new BlobSpot(cm.Type.Resolved, spot.Offset + cm.Offset)) switch
            {
                ulong cu => (long)Math.Min(cu, MostCap),
                long cl => cl,
                _ => -1L,
            };
            if (count < 0)
            {
                most[id] = -1;   // a stack of it whose count is unknown: no bound for this item
                return;
            }
            if (most.TryGetValue(id, out var had) && had < 0) return;
            most[id] = (int)Math.Min(MostCap, (long)Math.Max(0, had) + Math.Min(count, MostCap));
        }

        void Walk(BlobView v, BlobSpot spot, int depth)
        {
            if (depth > 10 || found.Count > 64) return;
            var t = spot.Type.Resolved;
            switch (t.Kind)
            {
                case GameTypeKind.Guid or GameTypeKind.ObjectReference when t.Size == 16:
                    if (v.Value(spot) is Guid g && byGuid.TryGetValue(g, out var id) && !found.Contains(id)) found.Add(id);
                    break;
                case GameTypeKind.U32:
                    if (v.Value(spot) is ulong or long && Convert.ToUInt32(v.Value(spot)) is var n && ids.Contains(n) && !found.Contains(n)) found.Add(n);
                    break;
                case GameTypeKind.Struct:
                    Stack(v, spot, t);
                    foreach (var f in t.AllFields) Walk(v, new BlobSpot(f.Type.Resolved, spot.Offset + f.Offset), depth + 1);
                    break;
                case GameTypeKind.StaticArray or GameTypeKind.BlobArray:
                    foreach (var e in v.Elements(spot).Take(128)) Walk(v, e, depth + 1);
                    break;
                case GameTypeKind.BlobOptional or GameTypeKind.BlobVariant:
                    if (v.Enter(spot) is { } inner) Walk(v, inner, depth + 1);
                    break;
            }
        }
    }
}
