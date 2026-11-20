// SPDX-License-Identifier: MIT
// The game data salvage's yield reads, from the running game's own resources (ReflectedResources + kiln.types): the
// keen::BalancingTable fields the server's Trash handler uses, and each gear item's EquipmentSlotType
// (ItemInfo.equipment.slot) for its cost factor. Read once per world; item slots on first use.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server.GameData;

internal sealed class SalvageData
{
    public SalvageBalancing? Balancing { get; private init; }
    public uint RuneItem { get; private init; }
    private readonly Dictionary<uint, string?> _slots = new();

    public static SalvageData Read(Action<string> log)
    {
        try
        {
            if (!ReflectedResources.Available || GameTypes.Find("keen::BalancingTable") is not { } type) return new SalvageData();
            foreach (var r in ReflectedResources.All(type.Resolved))
            {
                if (r.Part != 0) continue;
                var v = new BlobView(r.Bytes);
                var root = new BlobSpot(type.Resolved, 0);
                object? F(string name) => v.Value(v.Find(root, name));
                if (F("perkCostItem") is not Guid rune || rune == Guid.Empty) continue;
                var b = new SalvageBalancing(Convert.ToUInt32(F("perkUpgradeCostSeed")), Convert.ToSingle(F("perkCostCreep")),
                    Convert.ToUInt32(F("trashStart")), Convert.ToUInt32(F("trashEnd")), Convert.ToSingle(F("perkCostFactorWeaponsShield")),
                    Convert.ToSingle(F("perkCostFactorArmor")), Convert.ToSingle(F("perkCostFactorRings")), rune);
                return new SalvageData { Balancing = b, RuneItem = ItemId.FromGuidBytes(rune.ToByteArray()).Value };
            }
        }
        catch (Exception e)
        {
            log($"salvage: reading the BalancingTable failed ({e.Message})");
        }
        return new SalvageData();
    }

    /// <summary>The item's EquipmentSlotType name, or null.</summary>
    public string? SlotTypeOf(uint item, ItemTable items)
    {
        if (_slots.TryGetValue(item, out var known)) return known;
        string? slot = null;
        try
        {
            if (items.TryGet(item, out var row) && row.Guid != Guid.Empty && GameTypes.Find("keen::ItemInfo") is { } type &&
                ReflectedResources.Get(row.Guid, type.Resolved, 0) is { } r)
            {
                var v = new BlobView(r.Bytes);
                slot = v.Value(v.Find(new BlobSpot(type.Resolved, 0), "equipment.slot")) as string;
            }
        }
        catch (Exception) { slot = null; }
        _slots[item] = slot;
        return slot;
    }
}
