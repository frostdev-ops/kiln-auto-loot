// SPDX-License-Identifier: MIT
// The player's profiles: named sets of personal settings to switch between quickly (Ctrl+F10, a profile's own key, or the
// combo at the top of the F8 window). Kept in the player's game only, in profiles.json in the mod's data folder (our
// own format, versioned). The Kiln config kiln-auto-loot-client.json stays what it was, the settings in use: it always
// holds the active profile's settings, so a change in the window or by hand in the file goes into the active profile
// (UpdateActive), and switching copies the chosen profile into the config. The language is not part of a profile.
// Thread-safe: the window reads Snapshot from the render thread, the tick thread changes the book. Pure (no Kiln, no
// files: ProfileStore reads and writes it).
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

/// <summary>What the window shows of a profile.</summary>
/// <param name="Name">the player's name for it, or null for a built-in's own name (a text, in the window's language).</param>
/// <param name="Key">its own key (a Kiln key name), or null.</param>
/// <param name="BuiltIn">it is one of <see cref="BuiltInProfiles"/> (Reset puts it back).</param>
internal sealed record ProfileView(string Id, string? Name, string? Key, bool BuiltIn)
{
    public string Display(bool german) => Name ?? Texts.Get("profile." + Id, german);
}

internal sealed class ProfileBook
{
    public const string Format = "kiln-auto-loot-profiles";
    public const int Version = 1;
    public const int MaxNameLength = 40;
    public const int MaxProfiles = 64;

    private sealed class Profile
    {
        public required string Id;
        public string? Name;
        public string? Key;
        public PersonalSettings Settings = new();
        public ProfileView View => new(Id, Name, Key, BuiltInProfiles.IsBuiltIn(Id));
    }

    private readonly object _lock = new();
    private readonly List<Profile> _profiles = [];
    private string _active = BuiltInProfiles.Everything;
    private volatile ProfileSnapshot _snapshot = new([], null!);

    /// <summary>The profiles in order and the active one, as one value (any thread).</summary>
    public sealed record ProfileSnapshot(IReadOnlyList<ProfileView> Profiles, ProfileView Active);

    public ProfileSnapshot Snapshot => _snapshot;

    private ProfileBook() { }

    /// <summary>The first start: the built-ins. When the settings in use are not the defaults, they become a profile of
    /// their own ("My settings"), active, so nothing the player chose before is lost.</summary>
    public static ProfileBook Seed(PersonalSettings current, string mineName)
    {
        var book = new ProfileBook();
        foreach (var id in BuiltInProfiles.Ids)
            book._profiles.Add(new Profile { Id = id, Settings = BuiltInProfiles.Settings(id) });
        var mine = Comparable(current);
        if (mine == Comparable(new PersonalSettings())) book._active = BuiltInProfiles.Everything;
        else
        {
            book._profiles.Insert(0, new Profile { Id = "mine", Name = Name(mineName) ?? "My settings", Settings = current.Clone() });
            book._active = "mine";
        }
        book.Publish();
        return book;
    }

    // ---------------------------------------------------------------- reading

    public ProfileView Active => _snapshot.Active;

    /// <summary>The settings a profile holds (a copy), or null for an unknown id.</summary>
    public PersonalSettings? SettingsOf(string id)
    {
        lock (_lock) return Find(id)?.Settings.Clone();
    }

    /// <summary>The profile after the active one, wrapping round (Ctrl+F10).</summary>
    public string NextId()
    {
        lock (_lock)
        {
            var i = _profiles.FindIndex(p => p.Id == _active);
            return _profiles[(i + 1) % _profiles.Count].Id;
        }
    }

    /// <summary>The profile, other than <paramref name="exceptId"/>, whose key is <paramref name="key"/>; null = none.</summary>
    public ProfileView? KeyOwner(string? key, string? exceptId)
    {
        if (ProfileKeys.Normalize(key) is not { } k) return null;
        lock (_lock)
            return _profiles.FirstOrDefault(p => p.Id != exceptId && ProfileKeys.Same(p.Key, k))?.View;
    }

    // ---------------------------------------------------------------- changing (each returns what the caller applies)

    /// <summary>The settings in use changed (the window, F9, the file): they are the active profile's now. True when the
    /// profile changed (save the book).</summary>
    public bool UpdateActive(PersonalSettings inUse)
    {
        lock (_lock)
        {
            var p = Find(_active)!;
            if (Comparable(p.Settings) == Comparable(inUse)) return false;
            p.Settings = inUse.Clone();
            Publish();
            return true;
        }
    }

    /// <summary>Makes <paramref name="id"/> active. Returns the settings to put in use (the profile's, keeping the
    /// language in use), or null for an unknown id.</summary>
    public PersonalSettings? Switch(string id, PersonalSettings inUse)
    {
        lock (_lock)
        {
            if (Find(id) is not { } p) return null;
            _active = id;
            Publish();
            return WithLanguage(p.Settings, inUse);
        }
    }

    /// <summary>A new profile with the settings in use, made active (so nothing changes in use). Null when the name is
    /// empty, the key is another profile's or there are too many.</summary>
    public string? Add(string name, string? key, PersonalSettings inUse)
    {
        lock (_lock)
        {
            if (Name(name) is not { } n || _profiles.Count >= MaxProfiles) return null;
            var k = ProfileKeys.Normalize(key);
            if (k is not null && _profiles.Any(p => ProfileKeys.Same(p.Key, k))) return null;
            var id = NewId();
            _profiles.Add(new Profile { Id = id, Name = n, Key = k, Settings = inUse.Clone() });
            _active = id;
            Publish();
            return id;
        }
    }

    /// <summary>A new name and key. False for an unknown id, an empty name or a key that is another profile's.</summary>
    public bool Rename(string id, string name, string? key)
    {
        lock (_lock)
        {
            if (Find(id) is not { } p || Name(name) is not { } n) return false;
            var k = ProfileKeys.Normalize(key);
            if (k is not null && _profiles.Any(o => o.Id != id && ProfileKeys.Same(o.Key, k))) return false;
            // a built-in keeps its own (translated) name while the player leaves the name as it shows
            p.Name = BuiltInProfiles.IsBuiltIn(id) && (n == Texts.Get("profile." + id, false) || n == Texts.Get("profile." + id, true))
                ? null : n;
            p.Key = k;
            Publish();
            return true;
        }
    }

    /// <summary>Deletes a profile; never the last one. Deleting the active one makes the first remaining one active: then
    /// <paramref name="apply"/> holds its settings to put in use.</summary>
    public bool Delete(string id, PersonalSettings inUse, out PersonalSettings? apply)
    {
        apply = null;
        lock (_lock)
        {
            if (_profiles.Count <= 1 || Find(id) is not { } p) return false;
            _profiles.Remove(p);
            if (_active == id)
            {
                _active = _profiles[0].Id;
                apply = WithLanguage(_profiles[0].Settings, inUse);
            }
            Publish();
            return true;
        }
    }

    /// <summary>Puts a built-in back as it came (settings and name; its key stays). When it is active,
    /// <paramref name="apply"/> holds the settings to put in use.</summary>
    public bool Reset(string id, PersonalSettings inUse, out PersonalSettings? apply)
    {
        apply = null;
        lock (_lock)
        {
            if (!BuiltInProfiles.IsBuiltIn(id) || Find(id) is not { } p) return false;
            p.Settings = BuiltInProfiles.Settings(id);
            p.Name = null;
            if (_active == id) apply = WithLanguage(p.Settings, inUse);
            Publish();
            return true;
        }
    }

    // ---------------------------------------------------------------- the file form

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson()
    {
        lock (_lock)
        {
            var list = new JsonArray();
            foreach (var p in _profiles)
            {
                var o = new JsonObject { ["id"] = p.Id };
                if (p.Name is not null) o["name"] = p.Name;
                if (p.Key is not null) o["key"] = p.Key;
                o["settings"] = JsonSerializer.SerializeToNode(p.Settings, JsonOptions);
                list.Add(o);
            }
            var root = new JsonObject
            {
                ["format"] = Format, ["version"] = Version, ["active"] = _active, ["profiles"] = list,
            };
            return root.ToJsonString(JsonOptions);
        }
    }

    /// <summary>The book from the file's text, or null when it is not a profile file of ours (bad JSON, a property
    /// written twice at the top, another format, no usable profile). Profiles without an id, with an id seen before or
    /// with a property written twice are dropped; an unknown active profile becomes the first one; a missing or bad
    /// settings object reads as the defaults.</summary>
    public static ProfileBook? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        // a JsonObject builds its properties when first read, and a property written twice throws only then: every read
        // of the document is guarded, not just the parse
        try
        {
            return Interpret(JsonNode.Parse(json) as JsonObject);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static ProfileBook? Interpret(JsonObject? root)
    {
        if (root is null || Str(root["format"]) != Format || root["profiles"] is not JsonArray list) return null;
        var book = new ProfileBook();
        foreach (var node in list)
        {
            try
            {
                book.Read(node);
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
            {
                // this entry only: the others still count
            }
        }
        if (book._profiles.Count == 0) return null;
        var active = Str(root["active"]);
        book._active = book.Find(active ?? "") is not null ? active! : book._profiles[0].Id;
        book.Publish();
        return book;
    }

    /// <summary>One entry of the file's profile list (dropped when it has no usable id or the id is taken).</summary>
    private void Read(JsonNode? node)
    {
        if (node is not JsonObject o || ProfileKeys.SafeId(Str(o["id"])) is not { } id) return;
        if (_profiles.Count >= MaxProfiles || Find(id) is not null) return;
        PersonalSettings settings;
        try
        {
            settings = o["settings"]?.Deserialize<PersonalSettings>(JsonOptions) ?? new PersonalSettings();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            settings = new PersonalSettings();
        }
        var key = ProfileKeys.Normalize(Str(o["key"]));
        if (key is not null && _profiles.Any(p => ProfileKeys.Same(p.Key, key))) key = null;
        var name = Name(Str(o["name"]));
        if (name is null && !BuiltInProfiles.IsBuiltIn(id)) name = id;
        _profiles.Add(new Profile { Id = id, Name = name, Key = key, Settings = settings });
    }

    private static string? Str(JsonNode? n)
    {
        try
        {
            return n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- helpers (under the lock)

    private Profile? Find(string id) => _profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>A new id that was never used: the id names the profile's Kiln key binding, and Kiln keeps a rebound key
    /// for an id after its binding is gone, so a reused id would hand a deleted profile's key to a new one.</summary>
    private string NewId()
    {
        while (true)
        {
            var id = "p-" + Guid.NewGuid().ToString("N")[..12];
            if (Find(id) is null) return id;
        }
    }

    /// <summary>The next free "Profile N" number, for the window's New.</summary>
    public int NextNumber()
    {
        lock (_lock) return _profiles.Count(p => !BuiltInProfiles.IsBuiltIn(p.Id)) + 1;
    }

    private void Publish()
    {
        var views = _profiles.Select(p => p.View).ToArray();
        _snapshot = new ProfileSnapshot(views, views.First(v => v.Id == _active));
    }

    /// <summary>A name trimmed and cut to <see cref="MaxNameLength"/>; null when empty.</summary>
    internal static string? Name(string? name)
    {
        var t = TextRules.Trim((name ?? "").Replace('\n', ' ').Replace('\r', ' '));
        if (t.Length == 0) return null;
        return t.Length > MaxNameLength ? t[..MaxNameLength] : t;
    }

    private static PersonalSettings WithLanguage(PersonalSettings profile, PersonalSettings inUse)
    {
        var s = profile.Clone();
        s.Language = inUse.Language;
        return s;
    }

    /// <summary>Settings compared without the language (which is not part of a profile).</summary>
    private static string Comparable(PersonalSettings s)
    {
        var c = s.Clone();
        c.Language = "auto";
        return JsonSerializer.Serialize(c, JsonOptions);
    }
}
