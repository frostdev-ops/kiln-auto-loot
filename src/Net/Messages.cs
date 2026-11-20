// SPDX-License-Identifier: MIT
// The messages between the player's game (window, toasts) and the server world (looting), on channel
// "kiln-auto-loot:main". Registered in the same order on both sides (Channels.Open): PersonalMsg 0, ClientStateMsg 1,
// NoticeMsg 2, ServerInfoMsg 3, CommandMsg 4, SalvageCmdMsg 5, SalvageStateMsg 6 (new messages are only ever appended). In a hosted game the local player's messages never cross the network: the client
// part hands them to the server part in the same process (Context.Scheduler.Post), the way more-flame-altars does.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Net;

internal static class Channels
{
    public const string Name = "kiln-auto-loot:main";
    /// <summary>"3": the salvage messages (0.2.0). "2": PersonalMsg carries OnlyIds. A game with an older version then has
    /// no channel with this server and gets the server's defaults instead of failing to decode a message.</summary>
    public const string Version = "3";
    /// <summary>The whole PersonalSettings as JSON: the skip list, only list and carry limits can each be long, so allow
    /// 128 KiB (SettingsRules.FitsMessage checks a player's settings against it).</summary>
    public const int MaxJson = SettingsRules.MaxJsonBytes;
    /// <summary>At most this many item ids per list (skip list, only list) in one PersonalMsg (the game has a few
    /// thousand items).</summary>
    public const int MaxSkipIds = 8192;
    /// <summary>The largest message: a PersonalMsg with the longest JSON and every id of both lists, plus room for the
    /// lengths.</summary>
    public const int MaxMessageBytes = MaxJson + 2 * MaxSkipIds * 4 + 1024;
}

/// <summary>Player's game to server: the player's settings, whole (sent on join, after every change and when the
/// game's item table fills). SkipIds / OnlyIds: the item ids the player's game matched its skip list / only list to with
/// its own item names (English and German); a dedicated server knows internal names only, so it takes these ids for the
/// player.</summary>
public sealed record PersonalMsg(string Json, uint[] SkipIds, uint[] OnlyIds) : INetMessage<PersonalMsg>
{
    /// <summary>Settings without only-list ids.</summary>
    public PersonalMsg(string Json, uint[] SkipIds) : this(Json, SkipIds, []) { }

    public void Encode(NetWriter w)
    {
        w.String(Json);
        Ids(w, SkipIds);
        Ids(w, OnlyIds);
    }

    public static PersonalMsg Decode(ref NetReader r)
    {
        var json = r.String(Channels.MaxJson);
        var skip = Ids(ref r);
        var only = Ids(ref r);
        return new(json, skip, only);
    }

    private static void Ids(NetWriter w, uint[]? ids)
    {
        ids ??= [];
        if (ids.Length > Channels.MaxSkipIds) ids = ids[..Channels.MaxSkipIds];
        w.Int32(ids.Length);
        foreach (var id in ids) w.UInt32(id);
    }

    private static uint[] Ids(ref NetReader r)
    {
        var count = r.Int32();
        if (count < 0 || count > Channels.MaxSkipIds) throw new NetFormatException("Too many item ids.");
        var ids = new uint[count];
        for (var i = 0; i < count; i++) ids[i] = r.UInt32();
        return ids;
    }
}

/// <summary>Player's game to server: a game window (chest, crafting, dialogue, inventory, pause menu) or the F8 window
/// is open, so no accept and no loot-all for this player.</summary>
public sealed record ClientStateMsg(bool WindowOpen) : INetMessage<ClientStateMsg>
{
    public void Encode(NetWriter w) => w.Bool(WindowOpen);
    public static ClientStateMsg Decode(ref NetReader r) => new(r.Bool());
}

/// <summary>Server to player's game: show a notice. ItemId names the item for LeftBehind.</summary>
public sealed record NoticeMsg(NoticeKind Kind, uint ItemId) : INetMessage<NoticeMsg>
{
    public void Encode(NetWriter w) => w.Byte((byte)Kind).UInt32(ItemId);
    public static NoticeMsg Decode(ref NetReader r)
    {
        var kind = (NoticeKind)r.Byte();
        var item = r.UInt32();
        if (kind is not (NoticeKind.InventoryFull or NoticeKind.LeftBehind)) throw new NetFormatException("Unknown notice.");
        return new(kind, item);
    }
}

/// <summary>Server to player's game: what the server applies for this player after its limits (EffectiveJson, a
/// PersonalSettings), the limits themselves, and the world settings the window shows read-only. Enabled: the world runs
/// with the mod on (the value read at the world's start, the one in effect). LineOfSight: the server can check what a
/// player sees (otherwise visibleOnly has no effect, and the window says so). FocusItem: the item the player's gather
/// focus is on, 0 = none.</summary>
public sealed record ServerInfoMsg(string EffectiveJson, string LimitsJson, int MaxStack, bool BackpackCheat, bool Hosted,
    bool Enabled, bool LineOfSight, uint FocusItem) : INetMessage<ServerInfoMsg>
{
    public void Encode(NetWriter w) =>
        w.String(EffectiveJson).String(LimitsJson).Int32(MaxStack).Bool(BackpackCheat).Bool(Hosted).Bool(Enabled)
         .Bool(LineOfSight).UInt32(FocusItem);
    public static ServerInfoMsg Decode(ref NetReader r) =>
        new(r.String(Channels.MaxJson), r.String(4096), r.Int32(), r.Bool(), r.Bool(), r.Bool(), r.Bool(), r.UInt32());
}

/// <summary>What a player's command asks the server to do.</summary>
public enum CommandKind : byte
{
    /// <summary>Gather focus on the item this player last took by hand; again = focus off.</summary>
    ToggleFocus = 1,
    /// <summary>Gather focus off.</summary>
    ClearFocus = 2,
}

/// <summary>Player's game to server: a command (the gather focus key). The server answers with a ServerInfoMsg.</summary>
public sealed record CommandMsg(CommandKind Kind) : INetMessage<CommandMsg>
{
    public void Encode(NetWriter w) => w.Byte((byte)Kind);
    public static CommandMsg Decode(ref NetReader r)
    {
        var kind = (CommandKind)r.Byte();
        if (kind is not (CommandKind.ToggleFocus or CommandKind.ClearFocus)) throw new NetFormatException("Unknown command.");
        return new(kind);
    }
}

/// <summary>What a player asks the server's salvage to do. Tokens name items as the server listed them
/// (SalvageRow.Token).</summary>
public enum SalvageCmd : byte
{
    /// <summary>Send me my gear (backpack) and the Salvage chests near me.</summary>
    List = 1,
    Mark = 2,
    Unmark = 3,
    /// <summary>Keep: never salvage it, rules included (the game has no lock of its own).</summary>
    Keep = 4,
    Unkeep = 5,
    ClearMarks = 6,
    /// <summary>Salvage exactly these (what the confirm dialog listed); each is checked again first.</summary>
    Salvage = 7,
}

/// <summary>Player's game to server: a salvage command. The server answers with a SalvageStateMsg.</summary>
public sealed record SalvageCmdMsg(SalvageCmd Kind, ulong[] Tokens) : INetMessage<SalvageCmdMsg>
{
    public const int MaxTokens = 512;

    public void Encode(NetWriter w)
    {
        var tokens = Tokens ?? [];
        if (tokens.Length > MaxTokens) tokens = tokens[..MaxTokens];
        w.Byte((byte)Kind).Int32(tokens.Length);
        foreach (var t in tokens) w.UInt64(t);
    }

    public static SalvageCmdMsg Decode(ref NetReader r)
    {
        var kind = (SalvageCmd)r.Byte();
        if (!Enum.IsDefined(kind)) throw new NetFormatException("Unknown salvage command.");
        var count = r.Int32();
        if (count < 0 || count > MaxTokens) throw new NetFormatException("Too many items.");
        var tokens = new ulong[count];
        for (var i = 0; i < count; i++) tokens[i] = r.UInt64();
        return new(kind, tokens);
    }
}

/// <summary>Whether salvage works for this player now, and if not why.</summary>
public enum SalvageReady : byte
{
    Ready = 0,
    /// <summary>The server switched it off (allowSalvage false), or the world runs with the mod off.</summary>
    Off = 1,
    /// <summary>Kiln's item data (saves.instance_data) or item reads are not available on the server.</summary>
    NoItemData = 2,
    /// <summary>The game's balancing data (the yield's numbers) could not be read.</summary>
    NoGameData = 3,
    /// <summary>A Trash by hand gave other runes than the formula: salvage stays off in this world (kiln.log).</summary>
    Mismatch = 4,
    /// <summary>The world just loaded: the saved salvages are settled first (about 10 s).</summary>
    Starting = 5,
}

[Flags]
public enum SalvageRowFlags : byte { None = 0, Marked = 1, Kept = 2, InChest = 4 }

/// <summary>One item as the server sees it. Token: the server's name for it in commands. Block: why it may not be
/// salvaged (None = it may); Runes: what it gives now.</summary>
public readonly record struct SalvageRow(ulong Token, uint Item, ushort Level, byte Rarity, SalvageRowFlags Flags,
    SalvageBlock Block, int Runes);

/// <summary>The last salvage request's result: salvaged items, runes given, items skipped and the first reason.</summary>
public readonly record struct SalvageOutcome(int Salvaged, int Runes, int Skipped, SalvageBlock Reason);

/// <summary>Server to player's game: the player's gear and Salvage chest contents, the rune item, runes still owed (not
/// given yet: no room), the last result, and whether a salvage is running.</summary>
public sealed record SalvageStateMsg(SalvageReady Ready, uint RuneItem, int OwedRunes, SalvageRow[] Rows, SalvageOutcome Last,
    bool Busy) : INetMessage<SalvageStateMsg>
{
    public const int MaxRows = 256;

    public void Encode(NetWriter w)
    {
        var rows = Rows ?? [];
        if (rows.Length > MaxRows) rows = rows[..MaxRows];
        w.Byte((byte)Ready).UInt32(RuneItem).Int32(OwedRunes).Bool(Busy);
        w.Int32(Last.Salvaged).Int32(Last.Runes).Int32(Last.Skipped).Byte((byte)Last.Reason);
        w.Int32(rows.Length);
        foreach (var row in rows)
            w.UInt64(row.Token).UInt32(row.Item).UInt16(row.Level).Byte(row.Rarity).Byte((byte)row.Flags).Byte((byte)row.Block)
             .Int32(row.Runes);
    }

    public static SalvageStateMsg Decode(ref NetReader r)
    {
        var ready = (SalvageReady)r.Byte();
        var rune = r.UInt32();
        var owed = r.Int32();
        var busy = r.Bool();
        var last = new SalvageOutcome(r.Int32(), r.Int32(), r.Int32(), (SalvageBlock)r.Byte());
        var count = r.Int32();
        if (count < 0 || count > MaxRows) throw new NetFormatException("Too many rows.");
        var rows = new SalvageRow[count];
        for (var i = 0; i < count; i++)
            rows[i] = new SalvageRow(r.UInt64(), r.UInt32(), r.UInt16(), r.Byte(), (SalvageRowFlags)r.Byte(), (SalvageBlock)r.Byte(),
                r.Int32());
        return new(ready, rune, owed, rows, last, busy);
    }
}
