// SPDX-License-Identifier: MIT
// 0.2.3 split "crops" into crops out in the world (the old key) and crops in a base (cropsInBase), and gave the critters
// (fishing bait swarms, under plants before) their own switch. A file or message from before keeps its choices: a
// missing cropsInBase takes the old crops value, a missing critters the plants value.
using System.Text.Json;
using System.Text.Json.Nodes;
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class SettingsMigrationTests
{
    [Fact]
    public void An_old_settings_object_carries_crops_and_plants_over()
    {
        var o = JsonNode.Parse("""{"crops": false, "plants": false, "radius": 9}""")!.AsObject();
        Assert.Equal([("cropsInBase", false), ("critters", false)], SettingsMigration.Missing(o));
        SettingsMigration.Apply(o);
        Assert.False(o["cropsInBase"]!.GetValue<bool>());
        Assert.False(o["critters"]!.GetValue<bool>());
    }

    [Fact]
    public void A_new_settings_object_is_left_as_it_is()
    {
        var o = JsonNode.Parse("""{"crops": false, "CropsInBase": true, "plants": false, "critters": true}""")!.AsObject();
        Assert.Empty(SettingsMigration.Missing(o));
        // nothing to carry over: the defaults apply
        Assert.Empty(SettingsMigration.Missing(JsonNode.Parse("{}")!.AsObject()));
        Assert.Empty(SettingsMigration.Missing(null));
    }

    [Fact]
    public void An_old_message_from_a_players_game_keeps_its_crops_choice()
    {
        var s = SettingsJson.Read("""{"crops": false, "plants": true}""")!;
        Assert.False(s.Crops);
        Assert.False(s.CropsInBase);
        Assert.True(s.Critters);
        var t = SettingsJson.Read("""{"crops": true, "plants": false}""")!;
        Assert.True(t.CropsInBase);
        Assert.False(t.Critters);
        Assert.Null(SettingsJson.Read("not json"));
    }

    [Fact]
    public void Old_profiles_keep_their_crops_and_plants_choice()
    {
        var json = """
            {"format": "kiln-auto-loot-profiles", "version": 1, "active": "mine", "profiles": [
              {"id": "mine", "settings": {"crops": false, "plants": false}},
              {"id": "new", "settings": {"crops": false, "cropsInBase": true, "plants": false, "critters": true}}]}
            """;
        var book = ProfileBook.FromJson(json)!;
        var mine = book.SettingsOf("mine")!;
        Assert.False(mine.CropsInBase);
        Assert.False(mine.Critters);
        var fresh = book.SettingsOf("new")!;
        Assert.True(fresh.CropsInBase);
        Assert.True(fresh.Critters);
    }

    [Fact]
    public void The_settings_files_are_read_with_their_comments()
    {
        var client = """
            {
              // Ripe crops from farm beds, also in your base.
              "crops": false,
              "plants": true, // trailing comment
            }
            """;
        Assert.Equal([("cropsInBase", false), ("critters", true)], SettingsMigration.FromFile(client, null));
        var server = """
            {
              "enabled": true,
              /* the defaults */
              "defaults": { "crops": true, "plants": false }
            }
            """;
        Assert.Equal([("defaults.cropsInBase", true), ("defaults.critters", false)], SettingsMigration.FromFile(server, "defaults"));
        Assert.Empty(SettingsMigration.FromFile(null, null));
        Assert.Empty(SettingsMigration.FromFile("{ broken", null));
        Assert.Empty(SettingsMigration.FromFile("""{"defaults": 3}""", "defaults"));
    }

    // ---- 0.2.8: line of sight decides what is in reach; the height window (4 m each way) and "Only what I can see"
    // (off) had other defaults before. A settings object without settingsVersion was written before: the old defaults
    // it still holds become the new ones, values the player changed stay.

    [Fact]
    public void Old_settings_holding_the_old_defaults_get_the_line_of_sight_defaults()
    {
        var o = JsonNode.Parse("""{"heightAbove": 4, "heightBelow": 4.0, "visibleOnly": false, "radius": 9}""")!.AsObject();
        Assert.Equal([("heightAbove", (object)0f), ("heightBelow", 0f), ("visibleOnly", true)], SettingsMigration.Moved(o));
        SettingsMigration.Apply(o);
        var s = o.Deserialize<PersonalSettings>(SettingsRules.JsonOptions)!;
        Assert.Equal((0f, 0f, true, 9f), (s.HeightAbove, s.HeightBelow, s.VisibleOnly, s.Radius));
        Assert.Equal(SettingsMigration.CurrentVersion, s.SettingsVersion);
    }

    [Fact]
    public void Values_a_player_changed_stay_and_new_settings_are_left_alone()
    {
        var changed = JsonNode.Parse("""{"heightAbove": 6, "heightBelow": 4, "visibleOnly": false}""")!.AsObject();
        Assert.Equal([("heightBelow", (object)0f), ("visibleOnly", true)], SettingsMigration.Moved(changed));
        var chose = JsonNode.Parse("""{"heightAbove": 4, "heightBelow": 4, "visibleOnly": true}""")!.AsObject();
        Assert.Equal([("heightAbove", (object)0f), ("heightBelow", 0f)], SettingsMigration.Moved(chose));
        // written by 0.2.8 or later: a player who sets 4 m and visibleOnly off now keeps that
        var current = JsonNode.Parse("""{"heightAbove": 4, "heightBelow": 4, "visibleOnly": false, "settingsVersion": 2}""")!.AsObject();
        Assert.Empty(SettingsMigration.Moved(current));
        Assert.Empty(SettingsMigration.Moved(JsonNode.Parse("{}")!.AsObject()));
        Assert.Empty(SettingsMigration.Moved(null));
    }

    [Fact]
    public void The_settings_files_and_profiles_get_the_new_defaults()
    {
        var client = """
            {
              // old file
              "visibleOnly": false,
              "heightAbove": 4,
              "heightBelow": 2,
            }
            """;
        Assert.Equal([("heightAbove", (object)0f), ("visibleOnly", true)], SettingsMigration.MovedInFile(client, null));
        var server = """{ "defaults": { "visibleOnly": false, "heightAbove": 4, "heightBelow": 4 } }""";
        Assert.Equal([("defaults.heightAbove", (object)0f), ("defaults.heightBelow", 0f), ("defaults.visibleOnly", true)],
            SettingsMigration.MovedInFile(server, "defaults"));
        Assert.Empty(SettingsMigration.MovedInFile("""{ "defaults": { "heightAbove": 4, "settingsVersion": 2 } }""", "defaults"));

        var book = ProfileBook.FromJson("""
            {"format": "kiln-auto-loot-profiles", "version": 1, "active": "mine", "profiles": [
              {"id": "mine", "settings": {"heightAbove": 4, "heightBelow": 8, "visibleOnly": false}}]}
            """)!;
        var mine = book.SettingsOf("mine")!;
        Assert.Equal((0f, 8f, true), (mine.HeightAbove, mine.HeightBelow, mine.VisibleOnly));
    }

    // ---- 0.2.8, version skew: settings that cross the network are never moved to the new defaults. An older game sends
    // what its window shows, and the server applies exactly that until the game updates (its file moves then, once). A
    // newer game reading an older server's answer shows what that server applies.

    private const string OldDefaults = """{"heightAbove": 4, "heightBelow": 4, "visibleOnly": false, "crops": false}""";

    [Fact]
    public void An_older_games_settings_are_applied_as_it_sends_them_every_time()
    {
        var rig = new LootServerTests.LoopRig();
        for (var i = 0; i < 2; i++)
        {
            rig.Loop.QueuePersonal(rig.Player, OldDefaults, []);
            Assert.Equal([(rig.Player, true)], rig.Loop.ApplyQueuedSettings());
            var s = rig.Loop.SettingsOf(rig.Player);
            Assert.Equal((4f, 4f, false), (s.HeightAbove, s.HeightBelow, s.VisibleOnly));
            Assert.False(s.CropsInBase);   // a key the older game does not send still starts from the one it replaces
            rig.Clock.NowMs += LootLoop.SettingsGapMs;
        }
    }

    [Fact]
    public void A_newer_game_shows_what_an_older_server_applies()
    {
        var link = new SettingsLink(new LootServer(new Data()));
        link.SetInfo(new ServerInfoMsg(OldDefaults, "{}", 0, false, false, true, true, 0));
        var effective = link.Info!.Effective!;
        Assert.Equal((4f, 4f, false), (effective.HeightAbove, effective.HeightBelow, effective.VisibleOnly));
        Assert.False(effective.CropsInBase);
    }

    [Fact]
    public void Settings_written_by_this_version_carry_their_version_across_the_network()
    {
        var mine = new PersonalSettings { HeightAbove = 4, HeightBelow = 4, VisibleOnly = false };
        var json = SettingsJson.Write(mine);
        Assert.Contains("\"settingsVersion\":2", json);
        var back = SettingsJson.Read(json)!;
        Assert.Equal((4f, 4f, false, 2), (back.HeightAbove, back.HeightBelow, back.VisibleOnly, back.SettingsVersion));
    }
}
