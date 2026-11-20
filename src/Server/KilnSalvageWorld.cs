// SPDX-License-Identifier: MIT
// Salvage's Kiln calls: the player's backpack gear with its facts (Items.Read, PerkContainerNew and Level through
// World, the gem through Gems, marks through InstanceData), the yield's game data (SalvageData), and the take / give
// requests (Items.TakeSlot, Items.Give). Tick thread, server world only.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server.GameData;

namespace KilnAutoLoot.Server;

internal sealed class KilnSalvageWorld(Data data, IClock clock, InstanceKey<SalvageMark>? key, Action<string> log) : ISalvageWorld
{
    private const string LedgerKey = "salvage-ledger";
    private const ushort GemSlotBit = 1 << 8;   // ItemStateFlag.HasWeaponGemSlot
    private readonly SalvageData _game = SalvageData.Read(log);
    private bool _gemsLogged;

    public long NowMs => clock.NowMs;
    public SalvageBalancing? Balancing => _game.Balancing;
    public uint RuneItem => _game.RuneItem;
    public string? SlotTypeOf(uint item) => _game.SlotTypeOf(item, data.Items);

    public ulong SteamIdOf(EntityId player)
    {
        try { return Players.Find(player)?.SteamId ?? 0; }
        catch (KilnUnavailableException) { return 0; }
    }

    private static InventoryContents? Inv(EntityId entity)
    {
        try { return Items.Read(entity); }
        catch (KilnUnavailableException) { return null; }
    }

    private static IReadOnlyList<Player> AllPlayers()
    {
        try { return Players.All; }
        catch (KilnUnavailableException) { return []; }
    }

    private static IEnumerable<InventorySlot> BackpackGear(InventoryContents inv) =>
        inv.Slots.Where(s => !s.IsEmpty && s.IsGear && !s.ItemEntity.IsNone);

    /// <summary>Stopped at the world's end: answers that come later change nothing (the next world has its own ledger).</summary>
    public bool Stopped { get; set; }

    /// <summary>The player's two action-bar pages (SlotSelection.actionbarEntityIds): their slots look like backpack
    /// slots in a read but are never salvaged. Empty when it cannot be read: then nothing counts as the backpack.</summary>
    private static HashSet<uint>? ActionBar(EntityId player)
    {
        try
        {
            if (GameTypes.Find("keen::ecs::SlotSelection") is not { } t) return null;
            var fields = t.Resolved.AllFields.Where(x => x.Name == "actionbarEntityIds").ToList();
            if (fields.Count != 1 || World.ReadBytes(player, "SlotSelection") is not { } bytes) return null;
            var f = fields[0];
            if (f.Offset + 8 > bytes.Length) return null;
            return [BitConverter.ToUInt32(bytes, f.Offset), BitConverter.ToUInt32(bytes, f.Offset + 4)];
        }
        catch (Exception) { return null; }
    }

    /// <summary>A slot of the backpack proper: Generic, not worn or held, not on the action bar (null bar = unknown: no).</summary>
    private static int _barLogged;

    private static bool InBackpack(InventorySlot s, HashSet<uint>? bar)
    {
        if (bar is null && Interlocked.Exchange(ref _barLogged, 1) == 0)
            AutoLootMod.ModContext?.Log.Warn("salvage: the action bar cannot be read on this build: nothing counts as backpack gear");
        return InBackpackOf(s, bar);
    }

    private static bool InBackpackOf(InventorySlot s, HashSet<uint>? bar) =>
        s.Category == InventoryCategory.Generic && !s.IsEquipped && bar is not null && !bar.Contains(s.Slot.Inventory.Value);

    private SalvageMark? MarkOf(InventorySlot s)
    {
        if (key is null) return null;
        try { return s.Ref.TryGet(key, out var m) ? m : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The facts and the check of one slot of the player's inventories.</summary>
    private SalvageItem Facts(EntityId player, InventorySlot s, HashSet<uint>? bar)
    {
        var item = s.Item.Value;
        var mark = MarkOf(s);
        var perks = World.TryRead<ushort>(s.ItemEntity, "PerkContainerNew", out var pc);
        int available = pc & 0xFF, unlocked = pc >> 8;
        var level = World.TryRead<LevelRaw>(s.ItemEntity, "Level", out var lv) && lv.Current > 0 ? (int)lv.Current : s.Level;
        // the game's weapon socket bit; sockets on other gear (a mod's) only Gems knows: asked for every item
        var hasSocket = (s.ItemState & GemSlotBit) != 0;
        bool? gem = false;
        try
        {
            if (Gems.Socket(s.Ref) is not null)
            {
                hasSocket = true;
                gem = true;
            }
        }
        catch (Exception e)
        {
            if (hasSocket) gem = null;
            if (!_gemsLogged)
            {
                _gemsLogged = true;
                log($"salvage: gem sockets cannot be read ({e.Message}): gear with a weapon socket is kept");
            }
        }
        var runes = 0;
        if (perks && _game.Balancing is { } b)
            runes = SalvageFormula.Yield(level, available, unlocked, SalvageFormula.GroupOf(SlotTypeOf(item)), b, Factors());
        var place = InBackpack(s, bar) ? SalvagePlace.Backpack : SalvagePlace.Elsewhere;
        var facts = new SalvageFacts(place, s.IsEquipped, mark?.Kept == true, data.Quest.Contains(item), perks, hasSocket, gem, runes);
        var name = data.Items.TryGet(item, out var row) ? (row.English.Length > 0 ? row.English : row.Internal) : "#" + item;
        var rarity = s.Rarity is { } r ? (int)r : -1;
        return new SalvageItem(player, s.Slot.Value, s.ItemEntity, item, level, rarity, SalvageCheck.Of(facts), runes, name);
    }

    private static SalvageFactors Factors()
    {
        try
        {
            if (!Settings.Ready) return SalvageFactors.GameDefaults;
            return new SalvageFactors(Convert.ToSingle(Settings.Get("perkCostFactor")), Convert.ToSingle(Settings.Get("perkUpgradeRecyclingFactor")));
        }
        catch (Exception) { return SalvageFactors.GameDefaults; }
    }

    public SalvageItem? Read(EntityId player, EntityId itemEntity)
    {
        if (Inv(player) is not { } inv) return null;
        var bar = ActionBar(player);
        foreach (var s in BackpackGear(inv))
            if (s.ItemEntity == itemEntity) return Facts(player, s, bar);
        return null;
    }

    public SalvageItem? AtSlot(EntityId player, EntityId inventory, int index)
    {
        if (Inv(player) is not { } inv) return null;
        var want = (ulong)inventory.Value | ((ulong)(uint)index << 32);
        var bar = ActionBar(player);
        foreach (var s in BackpackGear(inv))
            if (s.Slot.Value == want) return Facts(player, s, bar);
        return null;
    }

    /// <summary>The player's backpack gear as rows for the window, marked first.</summary>
    public SalvageRow[] Rows(EntityId player)
    {
        if (Inv(player) is not { } inv) return [];
        var rows = new List<SalvageRow>();
        var bar = ActionBar(player);
        foreach (var s in BackpackGear(inv))
        {
            if (!InBackpack(s, bar)) continue;
            var f = Facts(player, s, bar);
            var mark = MarkOf(s);
            var flags = (mark?.Marked == true ? SalvageRowFlags.Marked : 0) | (mark?.Kept == true ? SalvageRowFlags.Kept : 0);
            rows.Add(new SalvageRow(s.ItemEntity.Value, f.Item, (ushort)Math.Clamp(f.Level, 0, ushort.MaxValue),
                (byte)Math.Clamp(f.Rarity, 0, 255), flags, f.Block, f.Runes));
            if (rows.Count >= SalvageStateMsg.MaxRows) break;
        }
        return rows.OrderByDescending(r => (r.Flags & SalvageRowFlags.Marked) != 0).ToArray();
    }

    public int RuneCount(EntityId player) => RuneItem != 0 && Inv(player) is { } inv ? inv.Count(new ItemId(RuneItem)) : 0;

    /// <summary>Changes the marks of these items (only ones in the player's own backpack).</summary>
    public void Change(EntityId player, IReadOnlyCollection<EntityId> items, Action<SalvageMark> change)
    {
        if (key is null || Inv(player) is not { } inv) return;
        foreach (var s in BackpackGear(inv))
        {
            if (!items.Contains(s.ItemEntity)) continue;
            var m = MarkOf(s) ?? new SalvageMark();
            change(m);
            Store(s, m);
        }
    }

    public void ClearMarks(EntityId player)
    {
        if (key is null || Inv(player) is not { } inv) return;
        foreach (var s in BackpackGear(inv))
            if (MarkOf(s) is { Marked: true } m)
            {
                m.Marked = false;
                Store(s, m);
            }
    }

    private void Store(InventorySlot s, SalvageMark m)
    {
        if (key is null) return;
        try
        {
            if (!m.Marked && !m.Kept && m.Ticket is null) s.Ref.Remove(key);
            else s.Ref.Set(key, m);
        }
        catch (KilnUnavailableException e) { log($"salvage: item data is off ({e.Message})"); throw; }
    }

    public bool SetTicket(EntityId itemEntity, string? ticket)
    {
        if (key is null) return false;
        foreach (var p in AllPlayers())
        {
            if (Inv(p.Entity) is not { } inv) continue;
            foreach (var s in BackpackGear(inv))
            {
                if (s.ItemEntity != itemEntity) continue;
                try
                {
                    var m = MarkOf(s) ?? new SalvageMark();
                    m.Ticket = ticket;
                    Store(s, m);
                    return true;
                }
                catch (Exception e)
                {
                    log($"salvage: could not write the ticket on {s.Item.Value} ({e.Message})");
                    return false;
                }
            }
        }
        return false;
    }

    public void FitsGive(EntityId holder, uint item, int count, Action<bool> fits) => _ = FitsGiveAsync(holder, item, count, fits);

    private async Task FitsGiveAsync(EntityId holder, uint item, int count, Action<bool> fits)
    {
        bool ok;
        try { ok = (await Items.Give(holder, new ItemId(item), count, null, GiveOptions.DryRun | GiveOptions.AllOrNothing)).Status == ItemsStatus.Done; }
        catch (Exception e)
        {
            log($"salvage: room check failed ({e.Message})");
            ok = false;
        }
        fits(ok);
    }

    public void Take(SalvageItem item, Action<bool> done) => _ = TakeAsync(item, done);

    private async Task TakeAsync(SalvageItem item, Action<bool> done)
    {
        bool ok = false;
        try
        {
            if (!Stopped && Inv(item.Holder) is { } inv && inv.Slots.FirstOrDefault(s => s.ItemEntity == item.ItemEntity && s.Item.Value == item.Item) is { } slot
                && !slot.IsEmpty && InBackpack(slot, ActionBar(item.Holder)))
                ok = (await Items.TakeSlot(item.Holder, slot)).Status == ItemsStatus.Done;
        }
        catch (Exception e) { log($"salvage: the take of {item.Name} was refused ({e.Message})"); }
        done(ok);
    }

    public void Give(EntityId holder, uint item, int count, Action<bool> done) => _ = GiveAsync(holder, item, count, done);

    private async Task GiveAsync(EntityId holder, uint item, int count, Action<bool> done)
    {
        bool ok;
        try { ok = !Stopped && (await Items.Give(holder, new ItemId(item), count, null, GiveOptions.AllOrNothing | GiveOptions.Notify)).Status == ItemsStatus.Done; }
        catch (Exception e)
        {
            log($"salvage: giving {count} runes failed ({e.Message})");
            ok = false;
        }
        done(ok);
    }

    public void SaveLedger(SalvageLedger ledger)
    {
        if (Stopped) return;
        try { Kiln.WorldData.Set(LedgerKey, ledger); }
        catch (KilnUnavailableException e) { log($"salvage: the ledger could not be stored ({e.Message})"); }
    }

    public void Log(string line) => log(line);

    /// <summary>keen::ecs::Level (a UInt32Attribute): 8 bytes of header, then dataStorage[current, max].</summary>
#pragma warning disable CS0649 // filled by World.TryRead
    private struct LevelRaw
    {
        public ulong Header;
        public uint Current;
        public uint Max;
    }
#pragma warning restore CS0649
}
