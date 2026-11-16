// SPDX-License-Identifier: MIT
// The player's game: the F8 window, the F9 key, the profile keys (next profile, no key by default; one per profile
// when set), the gather focus key (F11), the notices, and telling the server when a game window is open.
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
    private KeyBinding? _menuKey, _toggleKey, _nextProfileKey, _focusKey;
    private readonly Dictionary<string, (KeyBinding Binding, string Key)> _profileKeys = new();   // tick thread
    private readonly FocusNotes _focus = new();
    private readonly KeyDebounce _debounce = new(new WallClock());
    private int _bouncesSaid;
    private (ClientState State, uint ClientSession, uint ServerSession) _session;
    private long _nextHandoffMs;   // hosted: when the settings may be handed over again (main thread)
    private int _itemsVersion;   // the item table version the skip ids last followed
    private bool _started;

    private bool German => Texts.German(_link.Personal.Language, GameLanguage.Current);
    private string MenuKeyName => _menuKey?.Key.ToString() ?? KeyNames.DefaultMenuKey;
    private string ToggleKeyName => _toggleKey?.Key.ToString() ?? KeyNames.DefaultAutoLootKey;
    private string NextProfileKeyName => _nextProfileKey?.Key.ToString() ?? ProfileKeys.DefaultNextProfileKey;
    private string FocusKeyName => _focusKey?.Key.ToString() ?? ProfileKeys.DefaultFocusKey;

    /// <summary>The mod's own keys as bound now (a profile's key may not be one of them).</summary>
    private IEnumerable<string> FixedKeyNames => [MenuKeyName, ToggleKeyName, NextProfileKeyName, FocusKeyName];

    /// <summary>From OnLoad (tick thread).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _link.Start(AutoLootMod.ModContext?.DataDirectory ?? ".", () => Texts.Get("profileMine", German));
        _window = new SettingsWindow(data, _link, new WindowKeys(() => MenuKeyName, () => ToggleKeyName,
            () => NextProfileKeyName, () => FocusKeyName, () => FixedKeyNames), _focus, SendCommand);
        _link.PersonalChanged += _window.Reload;
        _link.ProfilesChanged += SyncProfileKeys;
        _link.ProfileSwitched += ToastProfile;

        // F8 and F9 by default, rebindable in Kiln's Keys page; both also act while the window has the input
        _menuKey = KeyBindings.Register(MenuKeyId, KeyNames.DefaultMenuKey, OnMenuKey, "Settings window", Category);
        _menuKey.WhileCapturing = true;
        _toggleKey = KeyBindings.Register(ToggleKeyId, KeyNames.DefaultAutoLootKey, OnToggleKey, "Auto loot on or off", Category);
        _toggleKey.WhileCapturing = true;
        _nextProfileKey = KeyBindings.Register(ProfileKeys.NextProfileId, ProfileKeys.DefaultNextProfileKey, OnNextProfileKey,
            "Next profile", Category);
        _focusKey = KeyBindings.Register(ProfileKeys.FocusId, ProfileKeys.DefaultFocusKey, OnFocusKey,
            "Gather only the item last taken by hand (again: everything)", Category);
        SyncProfileKeys();

        AutoLootMod.Hub.NoticeReceived += OnNotice;
        AutoLootMod.Hub.ServerInfoReceived += OnServerInfo;
        server.LocalNotice += OnLocalNotice;
        server.LocalServerInfo += OnServerInfo;
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
        server.LocalServerInfo -= OnServerInfo;
        AutoLootMod.Hub.NoticeReceived -= OnNotice;
        AutoLootMod.Hub.ServerInfoReceived -= OnServerInfo;
        _menuKey?.Dispose();
        _toggleKey?.Dispose();
        _nextProfileKey?.Dispose();
        _focusKey?.Dispose();
        _menuKey = _toggleKey = _nextProfileKey = _focusKey = null;
        foreach (var (binding, _) in _profileKeys.Values) binding.Dispose();
        _profileKeys.Clear();
        _link.ProfilesChanged -= SyncProfileKeys;
        _link.ProfileSwitched -= ToastProfile;
        if (_window is not null) _link.PersonalChanged -= _window.Reload;
        _window?.Dispose();
        _window = null;
        _link.Stop();
    }

    // ---------------------------------------------------------------- keys (main thread)

    /// <summary>Whether this press of <paramref name="bindingId"/> acts (KeyDebounce). The first few presses it drops in
    /// a game are logged.</summary>
    private bool Pressed(string bindingId)
    {
        if (_debounce.Take(bindingId, out var since)) return true;
        if (Interlocked.Increment(ref _bouncesSaid) <= 5)
            AutoLootMod.ModContext?.Log.Info($"key {bindingId}: pressed again {since} ms after the press it took: counted " +
                "once (a key that bounces, or one press that reached the mod twice)");
        return false;
    }

    private void OnMenuKey()
    {
        if (_window is null || !Pressed(MenuKeyId)) return;
        switch (KeyActions.MenuKey(_window.IsOpen, Ui.MenuOpen))
        {
            case MenuKeyAction.Open: _window.Open(); break;
            case MenuKeyAction.Close: _window.Close(); break;
        }
    }

    private void OnToggleKey()
    {
        if (!Pressed(ToggleKeyId) || !KeyActions.ToggleActs(Ui.MenuOpen)) return;
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

    // ---------------------------------------------------------------- profiles

    private void OnNextProfileKey()
    {
        if (Pressed(ProfileKeys.NextProfileId) && KeyActions.ToggleActs(Ui.MenuOpen)) _link.NextProfile();
    }

    private void ToastProfile(ProfileView profile)
    {
        var german = Texts.German(_link.Personal.Language, GameLanguage.Current);
        Toast(NoticeText.Compose(Texts.Format("profileSwitched", german, profile.Display(german))), ToastKind.Info, dedupe: false);
    }

    /// <summary>Tick thread: one Kiln binding per profile with a key. A new key is registered (a rebind on Kiln's Keys
    /// page then wins over the profile's own key); a key changed in the window rebinds; a key removed or a profile
    /// deleted drops the binding.</summary>
    private void SyncProfileKeys()
    {
        if (!_started || _link.Profiles is not { } book) return;
        var wanted = book.Snapshot.Profiles.Where(p => p.Key is not null).ToDictionary(p => p.Id);
        foreach (var id in _profileKeys.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            _profileKeys[id].Binding.Dispose();
            _profileKeys.Remove(id);
        }
        foreach (var (id, profile) in wanted)
        {
            var key = profile.Key!;
            var bindingId = ProfileKeys.BindingId(id);
            try
            {
                if (_profileKeys.TryGetValue(id, out var had))
                {
                    if (had.Key == key) continue;
                    KeyBindings.Rebind(bindingId, key);
                    _profileKeys[id] = (had.Binding, key);
                    continue;
                }
                var target = id;
                var binding = KeyBindings.Register(bindingId, key, () =>
                {
                    if (Pressed(bindingId) && KeyActions.ToggleActs(Ui.MenuOpen)) _link.SwitchProfile(target);
                }, "Profile: " + profile.Display(false), Category);
                _profileKeys[id] = (binding, key);
            }
            catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException)
            {
                AutoLootMod.ModContext?.Log.Warn($"profile key '{key}' for {id}: {e.Message}");
            }
        }
    }

    // ---------------------------------------------------------------- gather focus

    private void OnFocusKey()
    {
        if (Pressed(ProfileKeys.FocusId) && KeyActions.ToggleActs(Ui.MenuOpen)) SendCommand(CommandKind.ToggleFocus);
    }

    /// <summary>Asks the server part for a gather-focus change (main or render thread). Hosted: in-process on the tick
    /// thread, checked against the world session like the settings; joined: a CommandMsg.</summary>
    private void SendCommand(CommandKind kind)
    {
        if (KClient.IsHosting)
        {
            var player = KClient.LocalPlayer;
            var session = KClient.ServerWorldSession;
            if (player.IsNone || server.ServerConfig is null) return;
            if (kind == CommandKind.ToggleFocus) _focus.Asked();
            _link.Post(() =>
            {
                if (KClient.IsHosting && player == KClient.LocalPlayer && session == KClient.ServerWorldSession)
                    server.SetLocalCommand(player, kind);
            });
            return;
        }
        if (!KClient.IsRemote) return;
        if (Network.Server is not { } peer || !peer.HasChannel(Channels.Name))
        {
            Toast(NoticeText.Compose(Texts.Get("focusNoServer", German)), ToastKind.Info);
            return;
        }
        if (kind == CommandKind.ToggleFocus) _focus.Asked();
        AutoLootMod.Hub.SendToServer(new CommandMsg(kind));
    }

    /// <summary>The server's answer (any thread): kept for the window, and the focus note.</summary>
    private void OnServerInfo(ServerInfoMsg m)
    {
        _link.SetInfo(m);
        var note = _focus.OnAnswer(m.FocusItem);
        if (note.Kind == FocusNoteKind.None) return;
        var german = German;
        var text = note.Kind switch
        {
            FocusNoteKind.On => Texts.Format("focusOn", german, ItemName(note.Item, german)),
            FocusNoteKind.Off => Texts.Get("focusOff", german),
            _ => Texts.Format("focusHint", german, FocusKeyName),
        };
        Toast(NoticeText.Compose(text), ToastKind.Info, dedupe: false);
    }

    private string ItemName(uint id, bool german) =>
        data.Items.TryGet(id, out var row) ? ItemTable.DisplayOf(row, german) : "#" + id;

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
        _focus.Forget();
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
            _focus.Forget();
            if (KClient.IsRemote) _link.Resend();
            else _link.Forget();
        }
        // hosted: the server part registers its config at ServerStarted; hand the settings over to each hosted world,
        // again every 250 ms until it has them (the host's player may not exist yet at the first try, and the server part
        // does nothing for the host until they are there). Settings too large for a message are never handed over
        // (SendIfChanged refuses them each time); TooLarge is no guard here: it stays set after a refused edit while
        // the saved settings fit.
        if (KClient.IsHosting && server.ServerConfig is not null && !KClient.LocalPlayer.IsNone &&
            _link.HandedSession != KClient.ServerWorldSession && Environment.TickCount64 >= _nextHandoffMs)
        {
            _nextHandoffMs = Environment.TickCount64 + 250;
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
