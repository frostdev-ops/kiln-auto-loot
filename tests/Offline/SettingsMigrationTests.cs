// SPDX-License-Identifier: MIT
// 0.2.3 split "crops" into crops out in the world (the old key) and crops in a base (cropsInBase), and gave the critters
// (fishing bait swarms, under plants before) their own switch. A file or message from before keeps its choices: a
// missing cropsInBase takes the old crops value, a missing critters the plants value.
using System.Text.Json.Nodes;
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
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
}
