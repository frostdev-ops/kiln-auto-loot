// SPDX-License-Identifier: MIT
// What each template gives, as item ids: the item of its pickup, and the items of its default inventory (the game's
// stock resource a bush, crop, heap or swarm is filled with). A stock resource is read once and shared by every
// template that names it.
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
        var stocks = new Dictionary<Guid, List<uint>>();
        var result = new List<TemplateFacts>(templates.Count);
        foreach (var t in templates)
        {
            var yield = new List<uint>();
            // a pickup names its item by reference (the id field is often 0 in the data)
            if (t.Value("PickupItem.item") is { } r && Guid.TryParse(r, out var g) && byGuid.TryGetValue(g, out var pid)) yield.Add(pid);
            else if (t.Value("PickupItem.itemId.value") is { } v && uint.TryParse(v, out var vid) && ids.Contains(vid)) yield.Add(vid);
            if (t.Value("DefaultInventory.defaultInventory") is { } s && Guid.TryParse(s, out var stock) && stock != Guid.Empty &&
                t.Value("DefaultInventory.type") is { } type)
            {
                if (!stocks.TryGetValue(stock, out var inside)) stocks[stock] = inside = ReadStock(stock, type, byGuid, ids);
                foreach (var id in inside) if (!yield.Contains(id)) yield.Add(id);
            }
            result.Add(yield.Count == 0 ? t : t with { Yield = yield });
        }
        return result;
    }

    /// <summary>The resource type of the stocks: the reference names a one-byte tag type, the data sits in a resource of
    /// this type under the same GUID (lab, game build 26430253: 420 of them).</summary>
    private const string StockType = "keen::ecs::DefaultInventoryResource";

    /// <summary>Every item a stock resource names, by reference or by id, in its order.</summary>
    private static List<uint> ReadStock(Guid stock, string typeName, Dictionary<Guid, uint> byGuid, HashSet<uint> ids)
    {
        var found = new List<uint>();
        try
        {
            ReflectedResource? res = null;
            foreach (var name in new[] { StockType, typeName })
                if (GameTypes.Find(name) is { } t && t.Resolved.Kind == GameTypeKind.Struct && ReflectedResources.Get(stock, t) is { } r)
                {
                    res = r;
                    break;
                }
            if (res is null) return found;
            var type = res.Type;
            var v = new BlobView(res.Bytes);
            Walk(v, new BlobSpot(type.Resolved, 0), 0);
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or ArgumentException) { }
        return found;

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
