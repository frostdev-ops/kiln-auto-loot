// SPDX-License-Identifier: MIT
// The messages between the player's game (window, toasts) and the server world (looting), on channel
// "kiln-auto-loot:main". Registered in the same order on both sides (Channels.Open): PersonalMsg 0, ClientStateMsg 1,
// NoticeMsg 2, ServerInfoMsg 3. In a hosted game the local player's messages never cross the network: the client
// part hands them to the server part in the same process (Context.Scheduler.Post), the way more-flame-altars does.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Net;

internal static class Channels
{
    public const string Name = "kiln-auto-loot:main";
    public const string Version = "1";
    /// <summary>The whole PersonalSettings as JSON: the ignore list can be long, so allow 64 KiB.</summary>
    public const int MaxJson = 64 * 1024;
    /// <summary>At most this many skip-list item ids in one PersonalMsg (the game has a few thousand items).</summary>
    public const int MaxSkipIds = 8192;
    /// <summary>The largest message: a PersonalMsg with the longest JSON and every id, plus room for the lengths.</summary>
    public const int MaxMessageBytes = MaxJson + MaxSkipIds * 4 + 1024;
}

/// <summary>Player's game to server: the player's settings, whole (sent on join, after every change and when the
/// game's item table fills). SkipIds: the item ids the player's game matched its skip list to with its own item names
/// (English and German); a dedicated server knows internal names only, so it takes these ids for the player.</summary>
public sealed record PersonalMsg(string Json, uint[] SkipIds) : INetMessage<PersonalMsg>
{
    public void Encode(NetWriter w)
    {
        var ids = SkipIds ?? [];
        if (ids.Length > Channels.MaxSkipIds) ids = ids[..Channels.MaxSkipIds];
        w.String(Json).Int32(ids.Length);
        foreach (var id in ids) w.UInt32(id);
    }

    public static PersonalMsg Decode(ref NetReader r)
    {
        var json = r.String(Channels.MaxJson);
        var count = r.Int32();
        if (count < 0 || count > Channels.MaxSkipIds) throw new NetFormatException("Too many skip-list ids.");
        var ids = new uint[count];
        for (var i = 0; i < count; i++) ids[i] = r.UInt32();
        return new(json, ids);
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
/// with the mod on (the value read at the world's start, the one in effect).</summary>
public sealed record ServerInfoMsg(string EffectiveJson, string LimitsJson, int MaxStack, bool BackpackCheat, bool Hosted,
    bool Enabled) : INetMessage<ServerInfoMsg>
{
    public void Encode(NetWriter w) =>
        w.String(EffectiveJson).String(LimitsJson).Int32(MaxStack).Bool(BackpackCheat).Bool(Hosted).Bool(Enabled);
    public static ServerInfoMsg Decode(ref NetReader r) =>
        new(r.String(Channels.MaxJson), r.String(4096), r.Int32(), r.Bool(), r.Bool(), r.Bool());
}
