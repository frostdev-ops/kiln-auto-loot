// SPDX-License-Identifier: MIT
// The player's settings in the player's game: kiln-auto-loot-client.json, saved at
// once on every change (LastWriteError is read before a change counts as saved) and handed to the server
// part: in a hosted game in-process (Context.Scheduler, checked against the world session), joined to a server as a
// PersonalMsg on the mod's channel. The world's settings (kiln-auto-loot-server.json) belong to the server part; in a
// hosted game the host's window changes them here through LootServer.ServerConfig.
// The profiles (ProfileBook, profiles.json) live here too: the config always holds the active profile's settings, so
// every change that reaches the config (window, F9, a hand edit) is copied into the active profile, and a switch puts
// the chosen profile's settings into the config and hands them over like any other change.
// Threads: every config call runs on the tick thread (Post); the window calls in from the render thread.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KClient = Kiln.Client;

namespace KilnAutoLoot.Client;

internal sealed class SettingsLink(Server.LootServer server)
{
    private ModConfig<PersonalSettings>? _cfg;
    private string? _sentJson;                 // tick thread: what the server has from us
    private int _sentItemsVersion = -1;        // the item table the skip ids of that send came from
    private uint _sentSession = uint.MaxValue; // the hosted world it went to
    private bool _warnedNoScheduler;

    /// <summary>Raised on the tick thread after the personal settings changed (window, F9, file edit).</summary>
    public event Action? PersonalChanged;

    /// <summary>Raised on the tick thread after the profiles changed (a new one, a rename, a key, a switch, a delete).</summary>
    public event Action? ProfilesChanged;

    private ProfileStore? _store;
    private ProfileBook? _book;

    /// <summary>The profiles, or null before Start (any thread).</summary>
    public ProfileBook? Profiles => _book;

    /// <summary>The last failure to write profiles.json, or null (any thread).</summary>
    public volatile string? ProfileError;

    /// <summary>The last save failure, or null after a save that worked (any thread).</summary>
    public volatile string? SaveError;

    /// <summary>The server's answer, read once (joined): what it applies for this player, its limits, the world.</summary>
    public sealed record ServerView(PersonalSettings? Effective, ServerLimits? Limits, int MaxStack, bool BackpackCheat,
        bool Enabled, bool LineOfSight, uint FocusItem);

    private volatile ServerView? _info;
    public ServerView? Info => _info;

    /// <summary>A ServerInfoMsg arrived (any thread).</summary>
    public void SetInfo(ServerInfoMsg m) =>
        _info = new(SettingsJson.Read(m.EffectiveJson), SettingsJson.ReadLimits(m.LimitsJson), m.MaxStack, m.BackpackCheat,
            m.Enabled, m.LineOfSight, m.FocusItem);

    public PersonalSettings Personal => _cfg?.Value ?? new PersonalSettings();

    /// <summary>The world's settings when this game hosts the world and the server part runs, else null.</summary>
    public ModConfig<ServerSettings>? World => KClient.IsHosting ? server.ServerConfig : null;

    /// <summary>Hosted: `enabled` as the running world started with it, else null.</summary>
    public bool? WorldEnabledInEffect => KClient.IsHosting ? server.EnabledInWorld : null;

    /// <summary>Tick thread (OnLoad).</summary>
    public void Start(string dataDirectory, Func<string> mineName)
    {
        _cfg = Config.Register<PersonalSettings>(ConfigKind.Client);
        _cfg.Changed += OnFileChanged;
        _store = new ProfileStore(dataDirectory);
        _book = _store.Load(_cfg.Value, mineName(), out var seeded, out var problem);
        if (problem is not null) AutoLootMod.ModContext?.Log.Warn("profiles: " + problem);
        // the config is the active profile's settings: an edit made while the game was closed counts for it
        if (_book.UpdateActive(_cfg.Value) || seeded) SaveProfiles();
    }

    public void Stop()
    {
        if (_cfg is not null) _cfg.Changed -= OnFileChanged;
        _cfg = null;
    }

    private void OnFileChanged(ConfigChangedEvent _)
    {
        FollowActive();
        PersonalChanged?.Invoke();
        SendIfChanged();
    }

    /// <summary>Tick thread: the settings in use go into the active profile (saved when that changed it).</summary>
    private void FollowActive()
    {
        if (_cfg is not null && _book is not null && _book.UpdateActive(_cfg.Value)) SaveProfiles();
    }

    private void SaveProfiles()
    {
        if (_store is null || _book is null) return;
        ProfileError = _store.Save(_book);
    }

    // ---------------------------------------------------------------- profiles (any thread; the work on the tick thread)

    /// <summary>Raised on the tick thread after a switch (the combo, F10, a profile's key) with the profile now active.</summary>
    public event Action<ProfileView>? ProfileSwitched;

    /// <summary>Switches to a profile: its settings go into the config and to the server. An unknown id does nothing.</summary>
    public void SwitchProfile(string id) => Post(() => SwitchNow(id));

    /// <summary>The profile after the active one (the next-profile key).</summary>
    public void NextProfile() => Post(() =>
    {
        if (_book is not null) SwitchNow(_book.NextId());
    });

    private void SwitchNow(string id)
    {
        if (_cfg is null || _book is null) return;
        FollowActive();   // whatever is in use belongs to the profile we leave
        if (_book.Switch(id, _cfg.Value) is not { } next) return;
        PutInUse(next);
        ProfileSwitched?.Invoke(_book.Active);
    }

    /// <summary>A new profile from the settings in use, made active.</summary>
    public void NewProfile(string name, string? key) => EditProfiles(book => book.Add(name, key, _cfg!.Value) is not null);

    public void RenameProfile(string id, string name, string? key) => EditProfiles(book => book.Rename(id, name, key));

    public void DeleteProfile(string id) => EditProfiles(book =>
    {
        if (!book.Delete(id, _cfg!.Value, out var apply)) return false;
        if (apply is not null) PutInUse(apply, saveBook: false);
        return true;
    });

    public void ResetProfile(string id) => EditProfiles(book =>
    {
        if (!book.Reset(id, _cfg!.Value, out var apply)) return false;
        if (apply is not null) PutInUse(apply, saveBook: false);
        return true;
    });

    private void EditProfiles(Func<ProfileBook, bool> edit) => Post(() =>
    {
        if (_cfg is null || _book is null) return;
        FollowActive();
        if (!edit(_book)) return;
        SaveProfiles();
        ProfilesChanged?.Invoke();
    });

    /// <summary>Tick thread: the settings of the now active profile into the config (every changed entry), to the server,
    /// and the window reloads. The book is active on the new profile already, so the config's change events copy into
    /// the right profile.</summary>
    private void PutInUse(PersonalSettings next, bool saveBook = true)
    {
        if (_cfg is null) return;
        SaveError = Apply(_cfg, SettingValues.Changed(_cfg.Value, next));
        FollowActive();
        if (saveBook)
        {
            SaveProfiles();
            ProfilesChanged?.Invoke();
        }
        PersonalChanged?.Invoke();
        SendIfChanged();
    }

    /// <summary>Runs <paramref name="work"/> on the tick thread.</summary>
    public bool Post(Action work)
    {
        if (AutoLootMod.Scheduler is { } s)
        {
            s.Post(work);
            return true;
        }
        if (!_warnedNoScheduler)
        {
            _warnedNoScheduler = true;
            AutoLootMod.ModContext?.Log.Warn("no scheduler yet: the settings are not saved");
        }
        return false;
    }

    /// <summary>Saves the settings the window edited (any thread): every changed entry with TrySet, then sends them.</summary>
    public void Commit(PersonalSettings edited)
    {
        var snapshot = edited.Clone();
        Post(() =>
        {
            if (_cfg is null) return;
            SaveError = Apply(_cfg, SettingValues.Changed(_cfg.Value, snapshot));
            FollowActive();
            SendIfChanged();
        });
    }

    /// <summary>Changes one personal entry on the tick thread, then sends (F9). <paramref name="done"/> gets the saved
    /// value of the whole settings (tick thread).</summary>
    public void Change(Func<PersonalSettings, PersonalSettings> edit, Action<PersonalSettings>? done = null) => Post(() =>
    {
        if (_cfg is null) return;
        var next = edit(_cfg.Value.Clone());
        SaveError = Apply(_cfg, SettingValues.Changed(_cfg.Value, next));
        FollowActive();
        SendIfChanged();
        done?.Invoke(_cfg.Value);
    });

    /// <summary>Hosted: sets one entry of the world's file (a world setting or a limit), then hands the settings over
    /// again so the server clamps them to the new limits.</summary>
    public void CommitWorld(string path, object value)
    {
        var session = KClient.ServerWorldSession;
        Post(() =>
        {
            if (!KClient.IsHosting || session != KClient.ServerWorldSession || server.ServerConfig is not { } world) return;
            SaveError = world.TrySet(path, value, out var error) ? null : error;
            _sentJson = null;
            SendIfChanged();
        });
    }

    /// <summary>TrySet per changed entry. TrySet is all or nothing: true means the file holds the value,
    /// false names why ("cannot write ..." included); LastWriteError is read too (an earlier write
    /// that failed and was not repeated). Null = every entry saved.</summary>
    private static string? Apply(ConfigFile file, IEnumerable<(string Path, object? Value)> changes)
    {
        string? failed = null;
        foreach (var (path, value) in changes)
            if (!file.TrySet(path, value, out var error)) failed ??= error;
        return failed ?? file.LastWriteError;
    }

    /// <summary>A new connection or a new hosted world: the next send goes out whatever was sent before (any thread).</summary>
    public void Resend() => Post(() =>
    {
        _sentJson = null;
        SendIfChanged();
    });

    /// <summary>The game's item table may have been filled (any thread): joined, the skip ids go out again.</summary>
    public void ItemsChanged() => Post(SendIfChanged);

    /// <summary>The item ids this game's item table (internal, English and German names) matches the skip list (or the
    /// only list, written the same way) to: a dedicated server knows internal names only, so it takes these for this
    /// player. Empty while the table is.</summary>
    internal static uint[] SkipIds(string skipList, ItemTable items)
    {
        var ids = new IgnoreList(skipList ?? "", items).Ids.ToArray();
        Array.Sort(ids);
        return ids.Length > Channels.MaxSkipIds ? ids[..Channels.MaxSkipIds] : ids;
    }

    /// <summary>Tick thread: hands the settings to the server part when they differ from what it has (joined: also when
    /// the item table changed since, so the skip and only ids follow it).</summary>
    public void SendIfChanged()
    {
        if (_cfg is null) return;
        var mine = _cfg.Value.Clone();
        var json = SettingsJson.Write(mine);
        if (KClient.IsHosting)
        {
            var player = KClient.LocalPlayer;
            var session = KClient.ServerWorldSession;
            if (player.IsNone || server.ServerConfig is null) return;           // the server part has not started yet
            if (json == _sentJson && session == _sentSession) return;
            // in a hosted game the scheduler's tick thread is the server world's: hand over in-process
            if (session != KClient.ServerWorldSession || player != KClient.LocalPlayer) return;
            server.SetLocalPersonal(player, mine);
            _sentJson = json;
            _sentSession = session;
            return;
        }
        var items = server.Data.Items;
        var version = items.Version;
        if (!KClient.IsRemote || (json == _sentJson && version == _sentItemsVersion)) return;
        if (Network.Server is not { } peer || !peer.HasChannel(Channels.Name)) return;  // a server without the mod
        if (AutoLootMod.Hub.SendToServer(new PersonalMsg(json, SkipIds(mine.SkipList, items), SkipIds(mine.OnlyList, items))))
        {
            _sentJson = json;
            _sentItemsVersion = version;
        }
    }

    /// <summary>Forget the server's answer (left the server or the world).</summary>
    public void Forget()
    {
        _info = null;
        Post(() =>
        {
            _sentJson = null;
            _sentSession = uint.MaxValue;
        });
    }

    /// <summary>What the server applies for this player: the server's answer, or in a hosted game the host's own limits.</summary>
    public PersonalSettings? Effective(PersonalSettings mine)
    {
        if (World is { } world) return SettingsRules.Clamp(mine, world.Value.Limits);
        return _info?.Effective;
    }

    public ServerLimits? Limits()
    {
        if (World is { } world) return world.Value.Limits;
        return _info?.Limits;
    }
}
