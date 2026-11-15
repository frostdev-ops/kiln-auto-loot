// SPDX-License-Identifier: MIT
// The skip list on a server that knows internal item names only (a dedicated server): the player's game matches its
// list with its own English and German names and sends the ids; the server adds them to what it matches itself.
using Kiln;
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class SkipIdsTests
{
    /// <summary>A chest with Stone (item 2, slot 11) and Apple (item 6, slot 12): the slots the loop moved.</summary>
    private static ulong[] MovedSlots(LoopRig rig)
    {
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);
        chest.Slots = [new ContainerSlot(11, 2, 5), new ContainerSlot(12, 6, 1)];
        rig.ScanAfter(100);
        if (rig.Game.Loots.Count > 0) return [11, 12];   // nothing kept: one loot-all
        return rig.Game.Moves.Select(m => m.Slot).ToArray();
    }

    [Fact]
    public void The_players_game_resolves_English_and_German_names_and_name_parts_to_ids()
    {
        var game = Fixtures.Items();
        Assert.Equal([6u], SettingsLink.SkipIds("=Apple", game));
        Assert.Equal([6u], SettingsLink.SkipIds("=Apfel", game));
        Assert.Equal([1u, 6u], SettingsLink.SkipIds("äste, =Food_Apple", game));
        Assert.Equal([1u, 2u, 3u, 8u], SettingsLink.SkipIds("#Materials", game));
        Assert.Empty(SettingsLink.SkipIds("=Wood Logs", game));
        Assert.Empty(SettingsLink.SkipIds("=Apple", ItemTable.Empty()));   // the table is not filled yet
    }

    [Fact]
    public void Without_ids_a_server_with_internal_names_only_cannot_match_a_display_name()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; }, items: Fixtures.ServerItems());
        Assert.Equal([11ul, 12ul], MovedSlots(rig));
    }

    [Fact]
    public void The_sent_ids_make_the_server_skip_an_English_entry()
    {
        var ids = SettingsLink.SkipIds("=Apple", Fixtures.Items());
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; }, items: Fixtures.ServerItems(), skipIds: ids);
        Assert.Equal([11ul], MovedSlots(rig));
        Assert.Contains(rig.Notices, n => n.Msg.Kind == NoticeKind.LeftBehind && n.Msg.ItemId == 6);
    }

    [Fact]
    public void The_sent_ids_make_the_server_skip_a_German_name_part()
    {
        var ids = SettingsLink.SkipIds("apfel", Fixtures.Items());
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "apfel"; }, items: Fixtures.ServerItems(), skipIds: ids);
        Assert.Equal([11ul], MovedSlots(rig));
    }

    [Fact]
    public void Entries_the_ids_do_not_cover_still_match_on_the_servers_internal_names()
    {
        // the player's game sent ids for "=Apple" only (an older table, say); "=Material_T1_Stone" matches on the server
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple, =Material_T1_Stone"; },
            items: Fixtures.ServerItems(), skipIds: [6]);
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);
        chest.Slots = [new ContainerSlot(11, 2, 5), new ContainerSlot(12, 6, 1), new ContainerSlot(13, 7, 1)];
        rig.ScanAfter(100);
        Assert.Equal([13ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    [Fact]
    public void Players_without_the_mod_get_the_Defaults_matched_on_internal_names()
    {
        var server = new ServerSettings();
        server.Defaults.OpenDelay = 0;
        server.Defaults.SkipList = "=Food_Apple";
        var rig = new LoopRig(server: server, items: Fixtures.ServerItems(), hasMod: false);
        Assert.Equal([11ul], MovedSlots(rig));
    }

    [Fact]
    public void Players_without_the_mod_get_the_Defaults_matched_on_English_where_the_server_has_it()
    {
        var server = new ServerSettings();
        server.Defaults.OpenDelay = 0;
        server.Defaults.SkipList = "=Apple";
        var rig = new LoopRig(server: server, items: Fixtures.ServerItems(english: true), hasMod: false);
        Assert.Equal([11ul], MovedSlots(rig));
    }

    [Fact]
    public void Unknown_entries_of_a_player_who_sent_ids_are_not_logged_on_the_server()
    {
        var rig = new LoopRig(s => s.SkipList = "=Apple", items: Fixtures.ServerItems(), skipIds: [6]);
        Assert.DoesNotContain(rig.KeyLines, l => l.Contains("match no known item"));

        var server = new ServerSettings();
        server.Defaults.SkipList = "=Wood Logs";
        var defaults = new LoopRig(server: server, items: Fixtures.ServerItems(), hasMod: false);
        Assert.Single(defaults.KeyLines, l => l.Contains("match no known item") && l.Contains("=Wood Logs"));
    }

    [Fact]
    public void Settings_that_do_not_read_keep_the_previous_ids()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; }, items: Fixtures.ServerItems(), skipIds: [6]);
        rig.Loop.SetPersonal(rig.Player, null, []);
        Assert.Equal([11ul], MovedSlots(rig));
    }

    [Fact]
    public void New_settings_without_ids_drop_the_old_ids()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; }, items: Fixtures.ServerItems(), skipIds: [6]);
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { OpenDelay = 0, SkipList = "" }, []);
        Assert.Equal([11ul, 12ul], MovedSlots(rig));
    }

    [Fact]
    public void A_table_filled_after_the_join_is_used_from_the_next_scan()
    {
        var server = new ServerSettings();
        server.Defaults.OpenDelay = 0;
        server.Defaults.SkipList = "=Food_Apple";
        var table = ItemTable.Empty();
        var rig = new LoopRig(server: server, items: table, hasMod: false);
        Assert.False(rig.Loop.Players[rig.Player].Ignore!.Contains(6));
        table.Fill(Fixtures.ServerItems().All);
        Assert.Equal([11ul], MovedSlots(rig));
    }
}
