// SPDX-License-Identifier: MIT
// The one channel of the mod, opened once per load with every message registered in the same order on both sides.
// The server part subscribes to PersonalReceived / ClientStateReceived and sends notices and server info; the client
// part subscribes to NoticeReceived / ServerInfoReceived and sends its settings and window state.
// Handlers on the server run on the tick thread; handlers in the player's game run on its client world's thread.
using Kiln;

namespace KilnAutoLoot.Net;

internal sealed class ChannelHub : IDisposable
{
    private NetChannel? _ch;

    public event Action<PersonalMsg, EntityId>? PersonalReceived;     // server: from a player's game
    public event Action<ClientStateMsg, EntityId>? ClientStateReceived;
    public event Action<NoticeMsg>? NoticeReceived;                    // player's game: from the server
    public event Action<ServerInfoMsg>? ServerInfoReceived;
    public event Action<CommandMsg, EntityId>? CommandReceived;   // server: from a player's game
    public event Action<SalvageCmdMsg, EntityId>? SalvageCmdReceived;   // server: from a player's game
    public event Action<SalvageStateMsg>? SalvageStateReceived;         // player's game: from the server

    /// <summary>Not required: players without the mod may join and get the server's defaults.</summary>
    public void Open()
    {
        if (_ch is not null) return;
        _ch = Network.Channel(Channels.Name, Channels.Version, required: false, maxMessageBytes: Channels.MaxMessageBytes)
            .Register<PersonalMsg>((m, ctx) => PersonalReceived?.Invoke(m, ctx.Player), NetDirection.ToServer)
            .Register<ClientStateMsg>((m, ctx) => ClientStateReceived?.Invoke(m, ctx.Player), NetDirection.ToServer)
            .Register<NoticeMsg>((m, _) => NoticeReceived?.Invoke(m), NetDirection.ToClient)
            .Register<ServerInfoMsg>((m, _) => ServerInfoReceived?.Invoke(m), NetDirection.ToClient)
            .Register<CommandMsg>((m, ctx) => CommandReceived?.Invoke(m, ctx.Player), NetDirection.ToServer)
            .Register<SalvageCmdMsg>((m, ctx) => SalvageCmdReceived?.Invoke(m, ctx.Player), NetDirection.ToServer)
            .Register<SalvageStateMsg>((m, _) => SalvageStateReceived?.Invoke(m), NetDirection.ToClient);
    }

    /// <summary>Whether this player's game has the mod (and so the channel).</summary>
    public static bool HasMod(EntityId player) => Network.PeerOf(player) is { } peer && peer.HasChannel(Channels.Name);

    public bool SendToServer<T>(T message) where T : INetMessage<T> => _ch?.SendToServer(message) ?? false;
    public bool SendTo<T>(EntityId player, T message) where T : INetMessage<T> => _ch?.SendTo(player, message) ?? false;

    public void Dispose()
    {
        _ch?.Dispose();
        _ch = null;
    }
}
