// SPDX-License-Identifier: MIT
// SettingsLink's profile switch, delete and reset against a settings file whose writes fail, and the refusal of settings
// too large for one message. The file is a fake (IPersonalConfig); profiles.json goes to a temporary folder.
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline.Client;

/// <summary>A settings file in memory. Fail: every write fails; FailPath: writes to that entry fail.</summary>
internal sealed class FakePersonalConfig : IPersonalConfig
{
    public PersonalSettings Value { get; private set; } = new();
    public bool Fail;
    public string? FailPath;
    public int Writes;
    public string? LastWriteError { get; private set; }

    public bool TrySet(string path, object? value, out string error)
    {
        if (Fail || path == FailPath)
        {
            error = LastWriteError = "cannot write kiln-auto-loot-client.json: read-only";
            return false;
        }
        var next = Value.Clone();
        SettingValues.Set(next, path, value);
        Value = next;
        Writes++;
        error = "";
        LastWriteError = null;
        return true;
    }
}

public class SettingsLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kal-link-" + Guid.NewGuid().ToString("N"));
    private readonly FakePersonalConfig _file = new();
    private readonly SettingsLink _link = new(new KilnAutoLoot.Server.LootServer(new Data()));
    private readonly List<string> _switched = [];

    public SettingsLinkTests()
    {
        _link.Attach(_file, _dir, "Mine");
        _link.ProfileSwitched += p => _switched.Add(p.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private ProfileBook Book => _link.Profiles!;

    private ProfileBook Saved => ProfileBook.FromJson(File.ReadAllText(Path.Combine(_dir, ProfileStore.FileName)))!;

    [Fact]
    public void A_switch_puts_the_profile_in_use_and_says_so()
    {
        Assert.True(_link.SwitchNow(BuiltInProfiles.Wood));
        Assert.Equal(BuiltInProfiles.Wood, Book.Active.Id);
        Assert.Equal(FilterMode.Only, _file.Value.ItemFilter);
        Assert.Equal(BuiltInProfiles.Settings(BuiltInProfiles.Wood).OnlyList, _file.Value.OnlyList);
        Assert.Equal([BuiltInProfiles.Wood], _switched);
        Assert.Equal(BuiltInProfiles.Wood, Saved.Active.Id);
        Assert.Null(_link.SwitchError);
    }

    [Fact]
    public void A_switch_whose_writes_fail_changes_no_profile_and_says_why()
    {
        _file.Fail = true;
        Assert.False(_link.SwitchNow(BuiltInProfiles.Wood));
        Assert.Equal(BuiltInProfiles.Everything, Book.Active.Id);
        Assert.Equal(BuiltInProfiles.Settings(BuiltInProfiles.Wood).OnlyList, Book.SettingsOf(BuiltInProfiles.Wood)!.OnlyList);
        Assert.Equal(FilterMode.Only, Saved.SettingsOf(BuiltInProfiles.Wood)!.ItemFilter);   // the file kept Wood too
        Assert.Equal(BuiltInProfiles.Everything, Saved.Active.Id);
        Assert.Empty(_switched);
        Assert.Equal("profileWriteFailed", _link.SwitchError?.TextKey);
        Assert.Contains("read-only", _link.SwitchError?.Detail);
    }

    [Fact]
    public void A_switch_that_fails_half_way_puts_the_previous_settings_back()
    {
        _file.FailPath = "onlyList";   // itemFilter is written before it
        Assert.False(_link.SwitchNow(BuiltInProfiles.Wood));
        Assert.Equal(FilterMode.Skip, _file.Value.ItemFilter);                          // written back
        Assert.Empty(SettingValues.Changed(new PersonalSettings(), _file.Value));
        Assert.Empty(SettingValues.Changed(new PersonalSettings(), Book.SettingsOf(BuiltInProfiles.Everything)!));
        Assert.Equal(FilterMode.Only, Book.SettingsOf(BuiltInProfiles.Wood)!.ItemFilter);
        Assert.Equal(BuiltInProfiles.Everything, Book.Active.Id);
        Assert.Empty(_switched);
    }

    [Fact]
    public void A_profile_with_a_value_out_of_range_is_not_switched_to_and_nothing_is_written()
    {
        var json = Book.ToJson().Replace("\"radius\": 15", "\"radius\": 999");
        File.WriteAllText(Path.Combine(_dir, ProfileStore.FileName), json);
        var file = new FakePersonalConfig();
        var link = new SettingsLink(new KilnAutoLoot.Server.LootServer(new Data()));
        link.Attach(file, _dir, "Mine");
        Assert.Equal(999f, link.Profiles!.SettingsOf(BuiltInProfiles.Plunder)!.Radius);
        Assert.False(link.SwitchNow(BuiltInProfiles.Plunder));
        Assert.Equal(0, file.Writes);
        Assert.Equal("profileOutOfRange", link.SwitchError?.TextKey);
        Assert.Equal(999f, link.Profiles.SettingsOf(BuiltInProfiles.Plunder)!.Radius);   // kept for the player to fix
        Assert.Equal(BuiltInProfiles.Everything, link.Profiles.Active.Id);
    }

    [Fact]
    public void Deleting_the_active_profile_when_the_switch_fails_deletes_nothing()
    {
        Assert.True(_link.SwitchNow(BuiltInProfiles.Stone));
        _file.Fail = true;
        Assert.False(_link.DeleteNow(BuiltInProfiles.Stone));
        Assert.Equal(BuiltInProfiles.Ids, Book.Snapshot.Profiles.Select(p => p.Id));
        Assert.Equal(BuiltInProfiles.Stone, Book.Active.Id);
        _file.Fail = false;
        Assert.True(_link.DeleteNow(BuiltInProfiles.Stone));
        Assert.Equal(BuiltInProfiles.Everything, Book.Active.Id);
        Assert.DoesNotContain(BuiltInProfiles.Stone, Saved.Snapshot.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void Resetting_the_active_built_in_when_the_writes_fail_resets_nothing()
    {
        Assert.True(_link.SwitchNow(BuiltInProfiles.Treasure));
        var edited = _file.Value.Clone();
        edited.Radius = 7;
        Assert.True(_link.CommitNow(edited));
        Assert.Equal(7f, Book.SettingsOf(BuiltInProfiles.Treasure)!.Radius);   // the change went into the active profile
        _file.Fail = true;
        Assert.False(_link.ResetNow(BuiltInProfiles.Treasure));
        Assert.Equal(7f, Book.SettingsOf(BuiltInProfiles.Treasure)!.Radius);
        _file.Fail = false;
        Assert.True(_link.ResetNow(BuiltInProfiles.Treasure));
        Assert.Equal(15f, _file.Value.Radius);
        Assert.Equal(15f, Book.SettingsOf(BuiltInProfiles.Treasure)!.Radius);
    }

    [Fact]
    public void Settings_too_large_for_one_message_are_refused_and_the_window_is_told()
    {
        string List(char c) => string.Join(", ", Enumerable.Range(0, SkipListBounds.MaxEntries)
            .Select(i => "=" + new string(c, SkipListBounds.MaxEntryLength - 8) + i.ToString("D4")));
        // German letters are written escaped (\u00e4, six bytes each), so lists that are each within their bounds can
        // still add up past one message
        var huge = new PersonalSettings { SkipList = List('ä'), OnlyList = List('ö'), CarryLimits = List('ü') };
        Assert.True(SettingsRules.ListsWithin(huge));           // each list alone is within its bounds
        Assert.False(SettingsRules.FitsMessage(huge));
        var reloads = 0;
        _link.PersonalChanged += () => reloads++;
        Assert.False(_link.CommitNow(huge));
        Assert.True(_link.TooLarge);
        Assert.Equal(0, _file.Writes);
        Assert.Equal(1, reloads);                                // the window takes the saved settings again
        Assert.True(_link.CommitNow(new PersonalSettings { Radius = 20 }));
        Assert.False(_link.TooLarge);
        Assert.Equal(20f, _file.Value.Radius);
    }
}
