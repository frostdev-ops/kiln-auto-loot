// SPDX-License-Identifier: MIT
// The player's settings in the player's game: kiln-auto-loot-client.json, saved at
// once on every change (LastWriteError is read before a change counts as saved) and handed to the server
// part: in a hosted game in-process (Context.Scheduler, checked against the world session), joined to a server as a
// PersonalMsg on the mod's channel. The world's settings (kiln-auto-loot-server.json) belong to the server part; in a
// hosted game the host's window changes them here through LootServer.ServerConfig.
// The profiles (ProfileBook, profiles.json) live here too: the config always holds the active profile's settings, so
// every change that reaches the config (window, F9, a hand edit) is copied into the active profile, and a switch puts
// the chosen profile's settings into the config and hands them over like any other change. A switch is all or
// nothing: the destination is checked first, the copy into the profile is held off while its settings are written, and
// the active profile changes only once every entry is saved; a failed write puts the previous settings back and leaves
// both profiles as they were. Settings too large for one message to the server are refused (TooLarge).
// Threads: every config call runs on the tick thread (Post); the window calls in from the render thread.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KClient = Kiln.Client;

namespace KilnAutoLoot.Client;

internal sealed class SettingsLink(Server.LootServer server)
{
    private ModConfig<PersonalSettings>? _kilnCfg;
    private IPersonalConfig? _cfg;
    private bool _following = true;            // tick thread: false while a switch writes its settings
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

    /// <summary>The last change (or the settings in the file) would not fit one message to the server: refused or not
    /// sent; the window says so (any thread).</summary>
    public volatile bool TooLarge;

    /// <summary>Why the last profile switch (or reset, or delete of the active profile) failed: a Texts key and the
    /// file's own error text, or null (any thread).</summary>
    public sealed record SwitchProblem(string TextKey, string? Detail = null);

    public volatile SwitchProblem? SwitchError;

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
        _kilnCfg = Config.Register<PersonalSettings>(ConfigKind.Client);
        _kilnCfg.Changed += OnFileChanged;
        Attach(new KilnPersonalConfig(_kilnCfg), dataDirectory, mineName());
    }

    /// <summary>The settings file and the profiles (Start; the offline tests with a fake file). Tick thread.</summary>
    internal void Attach(IPersonalConfig cfg, string dataDirectory, string mineName)
    {
        _cfg = cfg;
        _store = new ProfileStore(dataDirectory);
        _book = _store.Load(_cfg.Value, mineName, out var seeded, out var problem);
        if (problem is not null) AutoLootMod.ModContext?.Log.Warn("profiles: " + problem);
        // the config is the active profile's settings: an edit made while the game was closed counts for it
        if (_book.UpdateActive(_cfg.Value) || seeded) SaveProfiles();
    }

    public void Stop()
    {
        if (_kilnCfg is not null) _kilnCfg.Changed -= OnFileChanged;
        _kilnCfg = null;
        _cfg = null;
    }

    private void OnFileChanged(ConfigChangedEvent _)
    {
        if (!_following) return;   // a switch is writing its settings: it reloads and sends once it is done
        FollowActive();
        PersonalChanged?.Invoke();
        SendIfChanged();
    }

    /// <summary>Tick thread: the settings in use go into the active profile (saved when that changed it).</summary>
    private void FollowActive()
    {
        if (_following && _cfg is not null && _book is not null && _book.UpdateActive(_cfg.Value)) SaveProfiles();
    }

    private void SaveProfiles()
    {
        if (_store is null || _book is null) return;
        ProfileError = _store.Save(_book);
    }

    // ---------------------------------------------------------------- profiles (any thread; the work on the tick thread)

    /// <summary>Raised on the tick thread after a switch (the combo, the next-profile key, a profile's key) with the profile now active.</summary>
    public event Action<ProfileView>? ProfileSwitched;

    /// <summary>Switches to a profile: its settings go into the config and to the server. An unknown id does nothing.</summary>
    public void SwitchProfile(string id) => Post(() => SwitchNow(id));

    /// <summary>The profile after the active one (the next-profile key).</summary>
    public void NextProfile() => Post(() =>
    {
        if (_book is not null) SwitchNow(_book.NextId());
    });

    /// <summary>Tick thread: the switch itself. True when the profile is active and its settings are in use; false
    /// (SwitchError says why, nothing changed) otherwise.</summary>
    internal bool SwitchNow(string id)
    {
        if (_cfg is null || _book is null || _book.SettingsOf(id) is not { } target) return false;
        FollowActive();   // whatever is in use belongs to the profile we leave
        target.Language = _cfg.Value.Language;
        if (!TryPutInUse(target)) return false;
        _book.Switch(id, _cfg.Value);
        FollowActive();
        SwitchError = null;
        SaveProfiles();
        ProfilesChanged?.Invoke();
        ProfileSwitched?.Invoke(_book.Active);
        return true;
    }

    /// <summary>Tick thread: writes <paramref name="target"/> into the config without copying anything into a profile
    /// meanwhile. The settings must be valid (in range, lists within their bounds, small enough for one message), and
    /// every entry must be saved; else the previous settings are written back and SwitchError says why.</summary>
    private bool TryPutInUse(PersonalSettings target)
    {
        if (_cfg is null) return false;
        if (Invalid(target) is { } why)
        {
            SwitchError = new(why);
            return false;
        }
        var before = _cfg.Value.Clone();
        _following = false;
        try
        {
            var error = Apply(_cfg, SettingValues.Changed(_cfg.Value, target));
            if (error is null && SettingValues.Changed(_cfg.Value, target).Count == 0) return true;
            SwitchError = new("profileWriteFailed", error ?? "");
            var back = Apply(_cfg, SettingValues.Changed(_cfg.Value, before));
            SaveError = back;   // null when the previous settings are back in the file
            return false;
        }
        finally
        {
            _following = true;
            PersonalChanged?.Invoke();
            SendIfChanged();
        }
    }

    /// <summary>Why a profile's settings cannot be put in use as they are (a Texts key), or null when they can.</summary>
    internal static string? Invalid(PersonalSettings s)
    {
        var clamped = SettingsRules.Clamp(s, new ServerLimits());
        if (SettingValues.Changed(s, clamped).Count > 0) return "profileOutOfRange";
        if (!SettingsRules.ListsWithin(s)) return "profileListTooLong";
        if (!SettingsRules.FitsMessage(s)) return "profileTooLarge";
        return null;
    }

    /// <summary>A new profile from the settings in use, made active.</summary>
    public void NewProfile(string name, string? key) => EditProfiles(book => book.Add(name, key, _cfg!.Value) is not null);

    public void RenameProfile(string id, string name, string? key) => EditProfiles(book => book.Rename(id, name, key));

    /// <summary>Deleting the active profile switches to the first other one first; when that switch fails, nothing is
    /// deleted.</summary>
    public void DeleteProfile(string id) => Post(() => DeleteNow(id));

    internal bool DeleteNow(string id)
    {
        if (_cfg is null || _book is null) return false;
        var snap = _book.Snapshot;
        if (snap.Profiles.Count <= 1 || snap.Profiles.All(p => p.Id != id)) return false;
        if (snap.Active.Id == id && !SwitchNow(snap.Profiles.First(p => p.Id != id).Id)) return false;
        if (!_book.Delete(id, _cfg.Value, out _)) return false;
        SaveProfiles();
        ProfilesChanged?.Invoke();
        return true;
    }

    /// <summary>Resetting the active built-in puts its settings in use first; when that fails, nothing is reset.</summary>
    public void ResetProfile(string id) => Post(() => ResetNow(id));

    internal bool ResetNow(string id)
    {
        if (_cfg is null || _book is null || !BuiltInProfiles.IsBuiltIn(id) || _book.SettingsOf(id) is null) return false;
        FollowActive();
        if (_book.Active.Id == id)
        {
            var target = BuiltInProfiles.Settings(id);
            target.Language = _cfg.Value.Language;
            if (!TryPutInUse(target)) return false;
            SwitchError = null;
        }
        if (!_book.Reset(id, _cfg.Value, out _)) return false;
        SaveProfiles();
        ProfilesChanged?.Invoke();
        return true;
    }

    private void EditProfiles(Func<ProfileBook, bool> edit) => Post(() =>
    {
        if (_cfg is null || _book is null) return;
        FollowActive();
        if (!edit(_book)) return;
        SaveProfiles();
        ProfilesChanged?.Invoke();
    });


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
        Post(() => CommitNow(snapshot));
    }

    /// <summary>Tick thread: Commit's work. False when nothing was saved (refused as too large, or no file).</summary>
    internal bool CommitNow(PersonalSettings edited)
    {
        if (_cfg is null || Refused(edited)) return false;
        SaveError = Apply(_cfg, SettingValues.Changed(_cfg.Value, edited));
        FollowActive();
        SendIfChanged();
        return true;
    }

    /// <summary>A change that would make the settings too large for one message is refused (the window takes the saved
    /// settings again and says so). Tick thread.</summary>
    private bool Refused(PersonalSettings next)
    {
        if (SettingsRules.FitsMessage(next))
        {
            TooLarge = false;
            return false;
        }
        TooLarge = true;
        PersonalChanged?.Invoke();
        return true;
    }

    /// <summary>Changes one personal entry on the tick thread, then sends (F9). <paramref name="done"/> gets the saved
    /// value of the whole settings (tick thread).</summary>
    public void Change(Func<PersonalSettings, PersonalSettings> edit, Action<PersonalSettings>? done = null) => Post(() =>
    {
        if (_cfg is null) return;
        var next = edit(_cfg.Value.Clone());
        if (Refused(next)) return;
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
    private static string? Apply(IPersonalConfig file, IEnumerable<(string Path, object? Value)> changes)
    {
        string? failed = null;
        foreach (var (path, value) in changes)
            if (!file.TrySet(path, value, out var error)) failed ??= error;
        return failed ?? file.LastWriteError;
    }

    /// <summary>A new connection or a new hosted world: the next send goes out whatever was sent before (any thread).</summary>
    /// <summary>The hosted world the settings last went to (uint.MaxValue: none since the last world). Any thread.</summary>
    public uint HandedSession => Volatile.Read(ref _sentSession);

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
        // a hand-edited file too large for one message is not sent (the server would drop it); the window says so
        if (!SettingsRules.FitsMessage(mine))
        {
            TooLarge = true;
            return;
        }
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
