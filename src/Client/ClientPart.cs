// SPDX-License-Identifier: MIT
// The player's game: the F8 window, the F9 key, the notices, and telling the server when a game window is open.
// The parts: SettingsLink (the settings file and the handoff to the server part),
// SettingsWindow + IgnoreEditor (drawing), and the pure rules in KeyActions, WindowSignal, StackField, Notices.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KClient = Kiln.Client;
using Texts = KilnAutoLoot.Core.Texts;

namespace KilnAutoLoot.Client;

internal sealed class ClientPart(Data data, Server.LootServer server)
{
    private const string MenuKeyId = "kiln-auto-loot.menu", ToggleKeyId = "kiln-auto-loot.toggle";
    private const string Category = "Kiln Auto Loot";

    private readonly SettingsLink _link = new(server);
    private readonly WindowSignalSender _signal = new();
    private readonly ToastBook _toasts = new(new WallClock());
    private SettingsWindow? _window;
    private KeyBinding? _menuKey, _toggleKey;
    private (ClientState State, uint ClientSession, uint ServerSession) _session;
    private uint _handedSession = uint.MaxValue;
    private int _itemsVersion;   // the item table version the skip ids last followed
    private bool _started;

    private bool German => Texts.German(_link.Personal.Language, GameLanguage.Current);
    private string MenuKeyName => _menuKey?.Key.ToString() ?? KeyNames.DefaultMenuKey;
    private string ToggleKeyName => _toggleKey?.Key.ToString() ?? KeyNames.DefaultAutoLootKey;

    /// <summary>From OnLoad (tick thread).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _link.Start();
        _window = new SettingsWindow(data, _link, () => MenuKeyName, () => ToggleKeyName);
        _link.PersonalChanged += _window.Reload;

        // F8 and F9 by default, rebindable in Kiln's Keys page; both also act while the window has the input
        _menuKey = KeyBindings.Register(MenuKeyId, KeyNames.DefaultMenuKey, OnMenuKey, "Settings window", Category);
        _menuKey.WhileCapturing = true;
        _toggleKey = KeyBindings.Register(ToggleKeyId, KeyNames.DefaultAutoLootKey, OnToggleKey, "Auto loot on or off", Category);
        _toggleKey.WhileCapturing = true;

        AutoLootMod.Hub.NoticeReceived += OnNotice;
        AutoLootMod.Hub.ServerInfoReceived += _link.SetInfo;
        server.LocalNotice += OnLocalNotice;
        server.LocalServerInfo += _link.SetInfo;
        Network.ServerReady += OnServerReady;
        Network.Disconnected += OnDisconnected;
        KClient.Tick += OnClientTick;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        KClient.Tick -= OnClientTick;
        Network.ServerReady -= OnServerReady;
        Network.Disconnected -= OnDisconnected;
        server.LocalNotice -= OnLocalNotice;
        server.LocalServerInfo -= _link.SetInfo;
        AutoLootMod.Hub.NoticeReceived -= OnNotice;
        AutoLootMod.Hub.ServerInfoReceived -= _link.SetInfo;
        _menuKey?.Dispose();
        _toggleKey?.Dispose();
        _menuKey = _toggleKey = null;
        if (_window is not null) _link.PersonalChanged -= _window.Reload;
        _window?.Dispose();
        _window = null;
        _link.Stop();
    }

    // ---------------------------------------------------------------- keys (main thread)

    private void OnMenuKey()
    {
        if (_window is null) return;
        switch (KeyActions.MenuKey(_window.IsOpen, Ui.MenuOpen))
        {
            case MenuKeyAction.Open: _window.Open(); break;
            case MenuKeyAction.Close: _window.Close(); break;
        }
    }

    private void OnToggleKey()
    {
        if (!KeyActions.ToggleActs(Ui.MenuOpen)) return;
        _link.Change(p =>
        {
            p.Looting = KeyActions.Toggle(p.Looting).On;
            return p;
        }, saved =>
        {
            var german = Texts.German(saved.Language, GameLanguage.Current);
            Toast(NoticeText.Compose(Texts.Get(saved.Looting ? "toggleOn" : "toggleOff", german)), ToastKind.Info, dedupe: false);
        });
    }

    // ---------------------------------------------------------------- notices

    private void OnLocalNotice(EntityId player, NoticeMsg m)
    {
        if (player == KClient.LocalPlayer) OnNotice(m);
    }

    private void OnNotice(NoticeMsg m)
    {
        var german = German;
        string? name = m.Kind == NoticeKind.LeftBehind && data.Items.TryGet(m.ItemId, out var row) ? ItemTable.DisplayOf(row, german) : null;
        if (NoticeText.For(m.Kind, name, MenuKeyName, german, Texts.Get) is { } text)
            Toast(text, m.Kind == NoticeKind.InventoryFull ? ToastKind.Warning : ToastKind.Info);
    }

    /// <param name="dedupe">server notices: not again while the same one is on screen (Notices.cs).</param>
    private void Toast(string text, ToastKind kind, bool dedupe = true)
    {
        if (!dedupe || _toasts.ShouldShow(text)) Hud.Toast(text, kind, NoticeText.ToastMs / 1000.0);   // any thread
    }

    // ---------------------------------------------------------------- connection, world, window signal

    private void OnServerReady(NetPeer peer)
    {
        if (!peer.HasChannel(Channels.Name)) return;   // a server without the mod: nothing to tell it
        _signal.Reset();
        _link.Resend();
    }

    private void OnDisconnected(NetPeer _)
    {
        _signal.Reset();
        _link.Forget();
    }

    /// <summary>Client world step (main thread): a new world or connection, the hosted handoff, the window signal.</summary>
    private void OnClientTick(in TickInfo tick)
    {
        var now = (KClient.State, KClient.ClientWorldSession, KClient.ServerWorldSession);
        if (now != _session)
        {
            _session = now;
            _signal.Reset();
            // joined: the handshake (ServerReady) may come before the client world's first step, so keep the server's
            // answer and send again; hosted: the handoff below sends to the new world
            if (KClient.IsRemote) _link.Resend();
            else _link.Forget();
        }
        // hosted: the server part registers its config at ServerStarted; hand the settings over once per hosted world
        if (KClient.IsHosting && server.ServerConfig is not null && _handedSession != KClient.ServerWorldSession)
        {
            _handedSession = KClient.ServerWorldSession;
            _link.Resend();
        }
        // the item table filled (or filled again): the skip list's ids for the server follow it
        if (data.Items.Version != _itemsVersion)
        {
            _itemsVersion = data.Items.Version;
            _link.ItemsChanged();
        }
        var screen = Ui.Screens.Current;
        var open = WindowSignal.WindowOpen(Ui.Screens.Available, screen.Kind, screen.Hud,
            Ui.MenuOpen || Ui.OpenScreens.Count > 0, _window?.IsOpen ?? false);
        _signal.Update(open, SendWindowState);
    }

    /// <summary>While a game window is open the server sends no accept and no loot-all while this is true. False = try again.</summary>
    private bool SendWindowState(bool open)
    {
        if (KClient.IsHosting)
        {
            var player = KClient.LocalPlayer;
            var session = KClient.ServerWorldSession;
            if (player.IsNone || server.ServerConfig is null) return false;
            return _link.Post(() =>
            {
                if (KClient.IsHosting && player == KClient.LocalPlayer && session == KClient.ServerWorldSession)
                    server.SetLocalWindowOpen(player, open);
            });
        }
        if (!KClient.IsRemote) return true;
        if (Network.Server is not { } peer || !peer.HasChannel(Channels.Name)) return true;  // no mod there: nothing to pause
        return AutoLootMod.Hub.SendToServer(new ClientStateMsg(open));
    }
}
