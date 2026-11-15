// SPDX-License-Identifier: MIT
// A player's game that sends settings too often or too large: the skip-list bounds, one application per player per
// half second (the latest wins), and what applying a hostile list costs.
using System.Diagnostics;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class SettingsFloodTests
{
    private static string Json(string skipList, float radius = 10) =>
        SettingsJson.Write(new PersonalSettings { SkipList = skipList, Radius = radius });

    /// <summary>As many items as the lab's game has (4268), with names of the game's shape.</summary>
    internal static ItemTable LabSizeTable()
    {
        string[] groups = ["Materials", "Consumables", "Weapons", "Equipment", "Ammunition", "BuildTools", "Collectibles", "Tools"];
        var rows = Enumerable.Range(1, 4268).Select(i => new ItemRow((uint)i, $"{groups[i % groups.Length]}_T{i % 7}_Thing_{i:0000}_Variant",
            $"Thing number {i}", $"Ding Nummer {i}", true, groups[i % groups.Length], groups[i % groups.Length], Guid.Empty, 50));
        return ItemTable.FromRows(rows);
    }

    [Fact]
    public void The_bounds_count_entries_and_their_length()
    {
        Assert.True(SkipListBounds.Within(""));
        Assert.True(SkipListBounds.Within(string.Join(",", Enumerable.Repeat("=x", SkipListBounds.MaxEntries)) + ", , ,"));
        Assert.False(SkipListBounds.Within(string.Join(",", Enumerable.Repeat("#x", SkipListBounds.MaxEntries + 1))));
        Assert.True(SkipListBounds.Within(string.Join(",", Enumerable.Repeat("x", SkipListBounds.MaxNameParts))));
        Assert.False(SkipListBounds.Within(string.Join(",", Enumerable.Repeat("x", SkipListBounds.MaxNameParts + 1))));
        Assert.True(SkipListBounds.Within("=" + new string('a', SkipListBounds.MaxEntryLength - 1) + "  "));
        Assert.False(SkipListBounds.Within("ok, " + new string('a', SkipListBounds.MaxEntryLength + 1)));
        // the longest internal item name of the game (81 characters) fits with its "="
        Assert.True(SkipListBounds.Within("=Prop_Decoration_Comfort_T7_IlluminationFloor_AncientTemple_Brazier_03_A_Ground_On"));
        var over = string.Join(",", Enumerable.Repeat("x", SkipListBounds.MaxEntries + 5));
        Assert.True(SkipListBounds.Accepts(over, string.Join(",", Enumerable.Repeat("x", SkipListBounds.MaxEntries + 4))));
        Assert.False(SkipListBounds.Accepts(over, over + ",=y"));
    }

    [Fact]
    public void A_list_over_the_bounds_keeps_the_previous_settings_and_is_logged_once()
    {
        var rig = new LoopRig(s => { s.SkipList = "=Apple"; s.Radius = 12; });
        var hostile = string.Concat(Enumerable.Repeat("#x,", 21_000)) + "#y";
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { SkipList = hostile, Radius = 5 });
        rig.Loop.SetPersonal(rig.Player, new PersonalSettings { SkipList = hostile + "z", Radius = 6 });
        var p = rig.Loop.Players[rig.Player];
        Assert.Equal("=Apple", p.Settings.SkipList);
        Assert.Equal(12f, p.Settings.Radius);
        Assert.True(p.Ignore!.Contains(6));
        Assert.Single(rig.KeyLines, l => l.Contains("skip list over"));
    }

    [Fact]
    public void Settings_messages_apply_at_most_once_per_half_second_and_the_latest_wins()
    {
        var rig = new LoopRig();
        rig.Loop.QueuePersonal(rig.Player, Json("=Apple", 11), [6]);
        Assert.Single(rig.Loop.ApplyQueuedSettings());                         // the first one goes on the next tick
        Assert.Equal(11f, rig.Loop.SettingsOf(rig.Player).Radius);

        for (var i = 0; i < 50; i++) rig.Loop.QueuePersonal(rig.Player, Json($"=Material_T1_Stone, n{i}", 12), [2]);
        rig.Clock.NowMs += 100;
        Assert.Empty(rig.Loop.ApplyQueuedSettings());                          // within the gap: kept
        Assert.Equal("=Apple", rig.Loop.SettingsOf(rig.Player).SkipList);
        rig.Clock.NowMs += LootLoop.SettingsGapMs - 100;
        var applied = rig.Loop.ApplyQueuedSettings();
        Assert.Equal([(rig.Player, true)], applied);
        Assert.Equal("=Material_T1_Stone, n49", rig.Loop.SettingsOf(rig.Player).SkipList);   // the last of the 50
        Assert.True(rig.Loop.Players[rig.Player].Ignore!.Contains(2));
        Assert.False(rig.Loop.Players[rig.Player].Ignore!.Contains(6));
        Assert.Empty(rig.Loop.ApplyQueuedSettings());                          // nothing left

        rig.Loop.QueuePersonal(rig.Player, "{not json", []);
        rig.Clock.NowMs += LootLoop.SettingsGapMs;
        Assert.Equal([(rig.Player, false)], rig.Loop.ApplyQueuedSettings());   // bad JSON: the previous ones stay
        Assert.Equal("=Material_T1_Stone, n49", rig.Loop.SettingsOf(rig.Player).SkipList);
    }

    [Fact]
    public void A_hostile_64_KiB_list_costs_next_to_nothing()
    {
        var table = LabSizeTable();
        var hostile = string.Concat(Enumerable.Repeat("#x,", 21_000)) + "#changing";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(hostile) < Channels.MaxJson);
        var rig = new LoopRig(items: table);
        var json = Json(hostile);
        rig.Loop.QueuePersonal(rig.Player, json, []);
        rig.Loop.ApplyQueuedSettings();                                        // warm up
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            rig.Loop.QueuePersonal(rig.Player, json, []);
            rig.Clock.NowMs += LootLoop.SettingsGapMs;
            rig.Loop.ApplyQueuedSettings();
        }
        var perApply = sw.Elapsed.TotalMilliseconds / 20;
        Assert.True(perApply < 5, $"{perApply:F3} ms per application");   // the JSON read and the bounds check only
    }

    [Fact]
    public void The_largest_list_within_the_bounds_stays_cheap_on_a_lab_size_table()
    {
        var table = LabSizeTable();
        // the worst a list in bounds can be: 256 different groups and exact names (looked up), or the most name parts
        // (searched in every name) with lookups for the rest
        var lookups = string.Join(",", Enumerable.Range(0, SkipListBounds.MaxEntries).Select(i => i % 2 == 0 ? $"#g{i}" : $"=n{i}"));
        var parts = string.Join(",", Enumerable.Range(0, SkipListBounds.MaxEntries)
            .Select(i => i < SkipListBounds.MaxNameParts ? $"q{i}" : $"=n{i}"));
        Assert.True(SkipListBounds.Within(lookups) && SkipListBounds.Within(parts));
        _ = new IgnoreList(parts, table);   // warm up
        // the fastest of five runs: a busy test machine slows single runs down, never the best one
        double Best(string list)
        {
            var best = double.MaxValue;
            for (var run = 0; run < 5; run++)
            {
                var sw = Stopwatch.StartNew();
                _ = new IgnoreList(list, table);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        var lookupMs = Best(lookups);
        var partMs = Best(parts);
        Assert.True(lookupMs < 5, $"{lookupMs:F3} ms for 256 group and exact entries");
        // 32 name parts x 4268 items; once per half second per player at most. Before the bounds the worst list in
        // range cost 35-60 ms, so 25 ms still catches that coming back.
        Assert.True(partMs < 25, $"{partMs:F3} ms for {SkipListBounds.MaxNameParts} name parts and the rest lookups");
    }

    [Fact]
    public void Duplicate_and_group_entries_match_as_before()
    {
        var items = Fixtures.Items();
        var list = new IgnoreList("#Materials, #materials, =Apple, =apple, =Nope, =nope, äste, ÄSTE", items);
        Assert.Equal([1u, 2u, 3u, 6u, 8u], list.Ids.OrderBy(i => i).ToArray());
        Assert.Equal(["=Nope", "=nope"], list.UnknownEntries);
        Assert.Equal(8, list.Entries.Count);
        Assert.False(list.IsUnknown("=Pflanzenfaser"));
        Assert.True(list.IsUnknown("=Nope"));
    }
}

public sealed class SettingsPerTickTests
{
    [Fact]
    public void At_most_two_players_settings_are_applied_per_tick_the_others_on_the_next()
    {
        var rig = new LoopRig();
        var others = new[] { rig.Game.AddPlayer(), rig.Game.AddPlayer() };
        foreach (var p in others.Prepend(rig.Player)) rig.Loop.QueuePersonal(p, SettingsJson.Write(new PersonalSettings()), []);
        Assert.Equal(LootLoop.SettingsPerTick, rig.Loop.ApplyQueuedSettings().Count);
        Assert.Single(rig.Loop.ApplyQueuedSettings());
        Assert.Empty(rig.Loop.ApplyQueuedSettings());
    }
}
