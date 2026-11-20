// SPDX-License-Identifier: MIT
// Salvage's pure rules. Salvaging gear into runes is the game's own item menu Trash (ItemActionType 2): the server's
// handler destroys the slot and adds the yield in one transaction. Its yield, as that handler computes it (server
// 26430253, functions 0x93bab0 and the upgrade cost 0x93fa30), is SalvageFormula. The rest: the rules for new
// pickups (SalvageRules), what is never salvaged (SalvageCheck), the ledger that keeps a salvage whole across a save
// (SalvageLedger) and the book of world sources a pickup may come from (SalvageSources). No Kiln calls.
using System.Text.Json.Serialization;
using Kiln;

namespace KilnAutoLoot.Core;

/// <summary>The upgrade-cost factor an item gets by its EquipmentSlotType (the game's switch: weapons, tools and
/// shields; armour Head to Feet; rings; everything else 1).</summary>
public enum SlotGroup { Other, WeaponOrShield, Armor, Ring }

/// <summary>What a rule names: All = any gear (also tools and other gear with upgrades).</summary>
public enum SalvageKind { All, Weapons, Shields, Armor, Rings }

/// <summary>The fields of the game's keen::BalancingTable the yield reads. RuneItem: perkCostItem's GUID (Runes).</summary>
public sealed record SalvageBalancing(uint Seed, float Creep, uint TrashStart, uint TrashEnd, float WeaponsShield,
    float Armor, float Rings, Guid RuneItem);

/// <summary>The world's settings the yield reads: perkCostFactor and perkUpgradeRecyclingFactor.</summary>
public readonly record struct SalvageFactors(float PerkCostFactor, float RecyclingFactor)
{
    public static readonly SalvageFactors GameDefaults = new(1f, 0.5f);
}

public static class SalvageFormula
{
    /// <summary>The game's rounding: half away from zero, then truncated.</summary>
    public static int Round(float v) => v >= 0f ? (int)(v + 0.5f) : (int)(v - 0.5f);

    /// <summary>The factor group of an EquipmentSlotType, by its name (the game's switch maps 1-7 and Shield to the
    /// weapons factor, Armour_Head..Armour_Feet to armour, Armour_Ring to rings).</summary>
    public static SlotGroup GroupOf(string? slotType) => slotType switch
    {
        "MeleeWeapon" or "Tool" or "BuildTool" or "MidRangeWeapon" or "RangeWeapon" or "Arrow" or "Spell" or "Shield" => SlotGroup.WeaponOrShield,
        "Armour_Head" or "Armour_UpperBody" or "Armour_Arms" or "Armour_LowerBody" or "Armour_Feet" => SlotGroup.Armor,
        "Armour_Ring" => SlotGroup.Ring,
        _ => SlotGroup.Other,
    };

    /// <summary>The kind a rule names for an EquipmentSlotType; All for gear that is none of the four.</summary>
    public static SalvageKind KindOf(string? slotType) => slotType switch
    {
        "MeleeWeapon" or "MidRangeWeapon" or "RangeWeapon" or "Spell" => SalvageKind.Weapons,
        "Shield" => SalvageKind.Shields,
        "Armour_Head" or "Armour_UpperBody" or "Armour_Arms" or "Armour_LowerBody" or "Armour_Feet" => SalvageKind.Armor,
        "Armour_Ring" => SalvageKind.Rings,
        _ => SalvageKind.All,
    };

    private static float FactorOf(SlotGroup g, SalvageBalancing b) => g switch
    {
        SlotGroup.WeaponOrShield => b.WeaponsShield,
        SlotGroup.Armor => b.Armor,
        SlotGroup.Ring => b.Rings,
        _ => 1f,
    };

    /// <summary>The upgrade cost at level n (0x93fa30): (seed x creep x n + seed) x perkCostFactor x the slot factor.
    /// Float steps in the game's order.</summary>
    public static int Cost(int n, SlotGroup g, SalvageBalancing b, SalvageFactors f)
    {
        var scale = f.PerkCostFactor;
        if (g != SlotGroup.Other) scale *= FactorOf(g, b);
        var seed = (float)b.Seed;
        var x = seed * b.Creep;
        x *= n;
        x += seed;
        x *= scale;
        return Round(x);
    }

    /// <summary>The runes the game's Trash gives for gear of level L with A upgrades in all (availablePerks) and U of them
    /// unlocked (0x93bab0): A x cost(max(L - U, 1)), plus round(recycling x U x round(cost(L) x pct(L))) with
    /// pct(L) = trashStart + (trashEnd - trashStart) / 100 x L.</summary>
    public static int Yield(int level, int available, int unlocked, SlotGroup g, SalvageBalancing b, SalvageFactors f)
    {
        var baseRunes = Cost(Math.Max(level - unlocked, 1), g, b, f) * available;
        var sum = 0;
        if (unlocked > 0)
        {
            var span = (float)unchecked(b.TrashEnd - b.TrashStart);
            var pct = span / 100f * level + b.TrashStart;
            var each = Round(Cost(level, g, b, f) * pct);
            for (var i = 0; i < unlocked; i++) sum += each;
        }
        return Round(sum * f.RecyclingFactor) + baseRunes;
    }
}

/// <summary>One rule for new pickups: gear of this kind up to this rarity (and level; 0 = any) is marked or salvaged.</summary>
public sealed record SalvageRule(SalvageKind Kind, GearRarity UpTo, int MaxLevel, bool On);

public static class SalvageRules
{
    public const int MaxRules = 6;
    /// <summary>The highest level gear reaches: 55 and its upgrades.</summary>
    public const int MaxLevelCap = 60;

    private static readonly Dictionary<string, SalvageKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = SalvageKind.All, ["weapons"] = SalvageKind.Weapons, ["shields"] = SalvageKind.Shields,
        ["armor"] = SalvageKind.Armor, ["armour"] = SalvageKind.Armor, ["rings"] = SalvageKind.Rings,
    };

    /// <summary>"kind:rarity[:level][!]" entries, comma separated; "!" = the rule is off. Entries that do not read are
    /// left out; at most <see cref="MaxRules"/>.</summary>
    public static IReadOnlyList<SalvageRule> Parse(string? text)
    {
        var list = new List<SalvageRule>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var raw in text.Split(','))
        {
            if (list.Count >= MaxRules) break;
            var e = raw.Trim();
            var on = true;
            if (e.EndsWith('!'))
            {
                on = false;
                e = e[..^1].TrimEnd();
            }
            var parts = e.Split(':');
            if (parts.Length is < 2 or > 3) continue;
            if (!Kinds.TryGetValue(parts[0].Trim(), out var kind)) continue;
            if (!Enum.TryParse<GearRarity>(parts[1].Trim(), true, out var rarity) || !Enum.IsDefined(rarity)) continue;
            var level = 0;
            if (parts.Length == 3 && !int.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out level)) continue;
            list.Add(new SalvageRule(kind, rarity, Math.Clamp(level, 0, MaxLevelCap), on));
        }
        return list;
    }

    public static string Format(IEnumerable<SalvageRule> rules) => string.Join(", ", rules.Take(MaxRules).Select(r =>
        $"{r.Kind.ToString().ToLowerInvariant()}:{r.UpTo.ToString().ToLowerInvariant()}" +
        (r.MaxLevel > 0 ? ":" + r.MaxLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) : "") + (r.On ? "" : "!")));

    /// <summary>The rule takes gear of <paramref name="kind"/> (All matches every kind; a kind rule only its own) with a
    /// known rarity up to UpTo and a level up to MaxLevel (0 = any).</summary>
    public static bool Matches(SalvageRule r, SalvageKind kind, int rarity, int level)
    {
        if (!r.On || rarity < 0 || rarity > (int)GearRarity.Legendary) return false;
        if (r.Kind != SalvageKind.All && r.Kind != kind) return false;
        if (rarity > (int)r.UpTo) return false;
        return r.MaxLevel <= 0 || level <= r.MaxLevel;
    }

    public static SalvageRule? FirstMatch(IReadOnlyList<SalvageRule> rules, SalvageKind kind, int rarity, int level) =>
        rules.FirstOrDefault(r => Matches(r, kind, rarity, level));
}

/// <summary>Where an item is: the player's own backpack (the generic slots), a Salvage chest, anywhere else.</summary>
public enum SalvagePlace { Backpack, SalvageChest, Elsewhere }

/// <summary>Why an item may not be salvaged; None = it may.</summary>
public enum SalvageBlock : byte { None, Equipped, Kept, Quest, Gem, NoPerks, Elsewhere, NoYield, Changed }

/// <summary>What the check needs to know about one item. GemInSocket null: the socket could not be read.</summary>
public readonly record struct SalvageFacts(SalvagePlace Place, bool Equipped, bool Kept, bool Quest, bool HasPerks,
    bool HasSocket, bool? GemInSocket, int Runes);

public static class SalvageCheck
{
    /// <summary>The first reason that applies, in this order: worn, kept, quest, a gem (or a socket that cannot be
    /// read), no upgrade data (the game's Trash salvages only gear with it), not in the backpack or a Salvage chest,
    /// no runes.</summary>
    public static SalvageBlock Of(in SalvageFacts f)
    {
        if (f.Equipped) return SalvageBlock.Equipped;
        if (f.Kept) return SalvageBlock.Kept;
        if (f.Quest) return SalvageBlock.Quest;
        if (f.HasSocket && f.GemInSocket != false) return SalvageBlock.Gem;
        if (!f.HasPerks) return SalvageBlock.NoPerks;
        if (f.Place == SalvagePlace.Elsewhere) return SalvageBlock.Elsewhere;
        if (f.Runes <= 0) return SalvageBlock.NoYield;
        return SalvageBlock.None;
    }

    /// <summary>A chest is a Salvage chest when the name a player gave it (Rename) is the salvageChestName setting,
    /// ignoring case and outer spaces. An empty setting makes none.</summary>
    public static bool IsSalvageChest(string? chestName, string? setting)
    {
        var want = setting?.Trim() ?? "";
        if (want.Length == 0 || chestName is null) return false;
        return string.Equals(chestName.Trim(), want, StringComparison.OrdinalIgnoreCase);
    }
}

public enum LedgerState { Taking, Owed }

/// <summary>One salvage in flight. Ticket: also written on the item's own data before the take, so a load can tell
/// whether the take made it into the save.</summary>
public sealed class LedgerEntry
{
    public string Ticket { get; set; } = "";
    public ulong SteamId { get; set; }
    public string Item { get; set; } = "";
    public uint RuneItem { get; set; }
    public int Runes { get; set; }
    public LedgerState State { get; set; }
    public long CreatedMs { get; set; }
}

/// <summary>The salvages between "the item goes" and "the runes are in", saved with the world (WorldData): Taking
/// before the take, Owed after it, gone once the runes are given. Item loss and duplicates need a save inside that
/// window; the ledger settles both after a load (ResolveAfterLoad, then the owed runes are paid).</summary>
public sealed class SalvageLedger
{
    [JsonInclude] public List<LedgerEntry> Entries { get; private set; } = [];

    public LedgerEntry? Find(string ticket) => Entries.FirstOrDefault(e => e.Ticket == ticket);

    public string Begin(ulong steamId, string item, uint runeItem, int runes, long nowMs)
    {
        string ticket;
        do ticket = Guid.NewGuid().ToString("N")[..16];
        while (Find(ticket) is not null);
        Entries.Add(new LedgerEntry { Ticket = ticket, SteamId = steamId, Item = item, RuneItem = runeItem, Runes = runes,
            State = LedgerState.Taking, CreatedMs = nowMs });
        return ticket;
    }

    public void Taken(string ticket)
    {
        if (Find(ticket) is { } e) e.State = LedgerState.Owed;
    }

    public void TakeFailed(string ticket) => Entries.RemoveAll(e => e.Ticket == ticket && e.State == LedgerState.Taking);

    public void Paid(string ticket) => Entries.RemoveAll(e => e.Ticket == ticket);

    public IEnumerable<LedgerEntry> OwedTo(ulong steamId) =>
        Entries.Where(e => e.State == LedgerState.Owed && e.SteamId == steamId);

    /// <summary>After the world loaded: a Taking entry whose ticket is still on an item did not make the save before
    /// its take (dropped: the item is there); one whose ticket is on no item lost its item (now Owed).</summary>
    public (int Dropped, int NowOwed) ResolveAfterLoad(Func<string, bool> ticketOnAnItem)
    {
        int dropped = 0, owed = 0;
        foreach (var e in Entries.Where(e => e.State == LedgerState.Taking).ToList())
        {
            if (ticketOnAnItem(e.Ticket))
            {
                Entries.Remove(e);
                dropped++;
            }
            else
            {
                e.State = LedgerState.Owed;
                owed++;
            }
        }
        return (dropped, owed);
    }
}

/// <summary>Which world sources a player looted in the last moments (their own E, Auto Loot's own takes, walk-over
/// pickups), so the gear that arrives from one counts as "picked up from the world": what a rule may touch. A source
/// Auto Loot's tables do not know (a tombstone, a built chest) is never one.</summary>
public sealed class SalvageSources(IClock clock)
{
    public const long WindowMs = 3000;
    private readonly Dictionary<(EntityId Player, EntityId Source), long> _seen = new();

    public void Looted(EntityId player, EntityId source, bool world)
    {
        if (!world || source.IsNone) return;
        if (_seen.Count > 512) Prune();
        _seen[(player, source)] = clock.NowMs;
    }

    public bool FromWorld(EntityId player, EntityId source) =>
        !source.IsNone && _seen.TryGetValue((player, source), out var at) && clock.NowMs - at <= WindowMs;

    public void Forget(EntityId player)
    {
        foreach (var k in _seen.Keys.Where(k => k.Player == player).ToList()) _seen.Remove(k);
    }

    private void Prune()
    {
        var now = clock.NowMs;
        foreach (var (k, at) in _seen.ToList())
            if (now - at > WindowMs) _seen.Remove(k);
    }
}
