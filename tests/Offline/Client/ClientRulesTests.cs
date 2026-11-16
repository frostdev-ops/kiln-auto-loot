// SPDX-License-Identifier: MIT
// The client's pure rules: stack size field and slider, keys, the window-open signal,
// the settings JSON round trip (PersonalMsg / ServerInfoMsg), the clamp display, the layout, notices.
using Kiln;
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using Xunit;

namespace KilnAutoLoot.Offline.Client;

public class StackFieldTests
{
    [Theory]
    [InlineData("9999", 9999)]
    [InlineData("  250 ", 250)]
    [InlineData("+5", 5)]
    [InlineData("-3", 0)]
    [InlineData("65536", 65535)]
    [InlineData("99999999999999999999999", 65535)]
    [InlineData("-99999999999999999999999", 0)]
    [InlineData("0", 0)]
    public void Typed_text_is_a_clamped_number(string text, int expected) => Assert.Equal(expected, StackField.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12a")]
    [InlineData("1.5")]
    [InlineData("-")]
    [InlineData("1e3")]
    public void Not_a_number_keeps_the_value(string text) => Assert.Null(StackField.Parse(text));

    [Fact]
    public void Steps_are_100_and_1000_with_ctrl_and_clamped()
    {
        Assert.Equal(10099, StackField.Stepped(9999, +1, ctrl: false));
        Assert.Equal(8999, StackField.Stepped(9999, -1, ctrl: true));
        Assert.Equal(0, StackField.Stepped(50, -1, ctrl: false));
        Assert.Equal(65535, StackField.Stepped(65500, +1, ctrl: true));
    }

    [Fact]
    public void Slider_is_logarithmic_with_default_at_zero()
    {
        Assert.Equal(0, StackField.ToPosition(0));
        Assert.Equal(0, StackField.FromPosition(0));
        Assert.Equal(65535, StackField.FromPosition(StackField.Positions));
        Assert.Equal(StackField.Positions, StackField.ToPosition(65535));
        Assert.True(StackField.ToPosition(1) >= 1);                       // 1 is not Default
        Assert.InRange(StackField.FromPosition(StackField.ToPosition(9999)), 9900, 10100);
        for (var p = 1; p < StackField.Positions; p++)
            Assert.True(StackField.FromPosition(p) <= StackField.FromPosition(p + 1));   // never goes back
        Assert.Equal("Default", StackField.Label(0, "Default"));
        Assert.Equal("9999", StackField.Label(9999, "Default"));
    }
}

public class KeyActionTests
{
    [Fact]
    public void Menu_key_toggles_and_waits_while_the_Kiln_menu_is_open()
    {
        Assert.Equal(MenuKeyAction.Open, KeyActions.MenuKey(windowOpen: false, kilnMenuOpen: false));
        Assert.Equal(MenuKeyAction.Close, KeyActions.MenuKey(windowOpen: true, kilnMenuOpen: false));
        Assert.Equal(MenuKeyAction.Close, KeyActions.MenuKey(windowOpen: true, kilnMenuOpen: true));
        Assert.Equal(MenuKeyAction.None, KeyActions.MenuKey(windowOpen: false, kilnMenuOpen: true));
    }

    [Fact]
    public void Escape_closes_only_an_open_window()
    {
        Assert.True(KeyActions.EscapeCloses(true));
        Assert.False(KeyActions.EscapeCloses(false));
    }

    [Fact]
    public void Toggle_flips_auto_loot_with_the_matching_toast()
    {
        Assert.Equal((false, "toggleOff"), KeyActions.Toggle(true));
        Assert.Equal((true, "toggleOn"), KeyActions.Toggle(false));
        Assert.True(KeyActions.ToggleActs(kilnMenuOpen: false));
        Assert.False(KeyActions.ToggleActs(kilnMenuOpen: true));
    }

    [Fact]
    public void A_press_that_arrives_twice_counts_once()
    {
        var clock = new FakeClock { NowMs = 10_000 };
        var gate = new KeyDebounce(clock);
        Assert.True(gate.Take("menu", out _));
        clock.NowMs += 40;   // the F8 window opened and closed again 40 ms later on a player's PC
        Assert.False(gate.Take("menu", out var since));
        Assert.Equal(40, since);
        Assert.True(gate.Take("toggle", out _));   // another key is its own
        clock.NowMs = 10_000 + KeyDebounce.WindowMs - 1;
        Assert.False(gate.Take("menu", out _));
        clock.NowMs = 10_000 + KeyDebounce.WindowMs;   // counted from the press it took, not from the dropped ones
        Assert.True(gate.Take("menu", out _));
        clock.NowMs += KeyDebounce.WindowMs;
        Assert.True(gate.Take("menu", out _));
    }
}

public class WindowSignalTests
{
    [Fact]
    public void Only_plain_play_lets_auto_loot_run()
    {
        Assert.False(WindowSignal.WindowOpen(true, ScreenKind.InGame, HudScreen.InGame, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.Storage, HudScreen.StorageChest, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.InGame, HudScreen.StorageChest, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.Crafting, HudScreen.StationBasedCrafting, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.Dialog, HudScreen.NpcDialog, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.Inventory, HudScreen.Backpack, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.PauseMenu, HudScreen.SystemMenu, false, false));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.Map, HudScreen.Map, false, false));
    }

    [Fact]
    public void The_F8_window_and_Kiln_menu_pause_too()
    {
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.InGame, HudScreen.InGame, false, f8Open: true));
        Assert.True(WindowSignal.WindowOpen(true, ScreenKind.InGame, HudScreen.InGame, kilnMenuOpen: true, false));
    }

    [Fact]
    public void Unknown_screens_do_not_pause_for_ever()
    {
        Assert.False(WindowSignal.WindowOpen(false, ScreenKind.None, HudScreen.Invalid, false, false));
        Assert.True(WindowSignal.WindowOpen(false, ScreenKind.None, HudScreen.Invalid, false, true));
    }

    [Fact]
    public void Sends_only_changes_and_retries_a_failed_send()
    {
        var s = new WindowSignalSender();
        var sent = new List<bool>();
        bool ok = true;
        bool Send(bool v) { sent.Add(v); return ok; }
        s.Update(false, Send);
        s.Update(false, Send);
        s.Update(true, Send);
        Assert.Equal([false, true], sent);
        ok = false;
        s.Update(false, Send);
        s.Update(false, Send);                // failed before: tried again
        Assert.Equal([false, true, false, false], sent);
        ok = true;
        s.Reset();
        s.Update(false, Send);                // after a reset the same value goes out again
        Assert.Equal(5, sent.Count);
    }
}

public class SettingsJsonTests
{
    [Fact]
    public void Personal_settings_round_trip_with_camelCase_keys()
    {
        var p = new PersonalSettings
        {
            Looting = false, Radius = 23, OpenDelay = 0.3f, SkipList = "=Material_T1_Twigs, #Food, stone",
            Fish = true, FellTrees = true, ChestsRadius = 7, Language = "de",
        };
        var json = SettingsJson.Write(p);
        Assert.Contains("\"radius\":23", json);
        Assert.Contains("\"skipList\":", json);
        var back = SettingsJson.Read(json)!;
        Assert.Empty(SettingValues.Changed(p, back));
    }

    [Fact]
    public void Json_uses_the_settings_keys()
    {
        var json = SettingsJson.Write(new PersonalSettings());
        foreach (var key in new[] { "looting", "radius", "takeInPlace", "visibleOnly", "groundLoot", "stones", "oldBooks",
                     "collectibles", "plants", "crops", "fish", "skipList", "skipNotice", "groundRadius", "collectiblesRadius", "plantsRadius",
                     "cropsRadius", "corpsesRadius", "chestsRadius", "enemyPause", "enemyDistance", "corpses", "bosses",
                     "openDelay", "chests", "leaveQuestItems", "smash", "smashBuildables", "fellTrees", "smashHives", "language" })
            Assert.Contains($"\"{key}\":", json);
        var limits = SettingsJson.WriteLimits(new ServerLimits());
        foreach (var key in new[] { "maxRadius", "allowSmash", "allowFelling", "allowHives" })
            Assert.Contains($"\"{key}\":", limits);
        var all = SettingsJson.Read("{\"looting\":false,\"openDelay\":2.5,\"fellTrees\":true,\"skipList\":\"#Food\",\"cropsRadius\":12}")!;
        Assert.False(all.Looting);
        Assert.Equal(2.5f, all.OpenDelay);
        Assert.True(all.FellTrees);
        Assert.Equal("#Food", all.SkipList);
        Assert.Equal(12f, all.CropsRadius);
    }

    [Fact]
    public void Unknown_keys_are_ignored_and_bad_json_is_null()
    {
        var p = SettingsJson.Read("{\"radius\":5,\"somethingNew\":1}")!;
        Assert.Equal(5f, p.Radius);
        Assert.True(p.Looting);                               // missing = default
        Assert.Null(SettingsJson.Read("{not json"));
        Assert.Null(SettingsJson.Read(""));
        Assert.Null(SettingsJson.ReadLimits("[1,2]"));
    }

    [Fact]
    public void Limits_round_trip()
    {
        var l = new ServerLimits { MaxRadius = 20, AllowFelling = false };
        var back = SettingsJson.ReadLimits(SettingsJson.WriteLimits(l))!;
        Assert.Equal(20f, back.MaxRadius);
        Assert.False(back.AllowFelling);
        Assert.True(back.AllowSmash);
    }
}

public class ClampDisplayTests
{
    [Fact]
    public void Shows_what_the_server_applies_after_its_limits()
    {
        var mine = new PersonalSettings { Radius = 40, GroundRadius = 0, EnemyDistance = 10, FellTrees = true, Smash = true };
        var effective = SettingsRules.Clamp(mine, new ServerLimits { MaxRadius = 25, AllowFelling = false });
        var differing = SettingValues.Differing(mine, effective).Select(s => s.Key).ToList();
        Assert.Equal(["radius", "fellTrees"], differing);
        var radius = SettingCatalog.All.Single(s => s.Key == "radius");
        Assert.Equal("25 m", SettingValues.Describe(radius, effective, german: false));
        var trees = SettingCatalog.All.Single(s => s.Key == "fellTrees");
        Assert.Equal("aus", SettingValues.Describe(trees, effective, german: true));
    }

    [Fact]
    public void Values_are_formatted_and_rounded_as_saved()
    {
        Assert.Equal("15 m", SettingValues.Format(WidgetKind.Meters, 14.6f));
        Assert.Equal("1.0 s", SettingValues.Format(WidgetKind.Seconds, 0.96f));
        Assert.Equal(0.3f, SettingValues.RoundSeconds(0.27f));
        Assert.Equal(3f, SettingValues.RoundMeters(2.5f));
    }

    [Fact]
    public void Changed_lists_camelCase_paths()
    {
        var a = new PersonalSettings();
        var b = a.Clone();
        b.Stones = false;
        b.OpenDelay = 2.5f;
        Assert.Equal([("openDelay", (object?)2.5f), ("stones", false)], SettingValues.Changed(a, b).OrderBy(c => c.Path));
    }
}

public class CatalogTests
{
    [Fact]
    public void Pages_in_order_with_their_settings()
    {
        Assert.Equal(["looting", "containers", "smashing", "filters", "where", "ranges", "world"], SettingCatalog.Pages.Select(p => p.Key));
        string[] On(string page) => SettingCatalog.Pages.Single(p => p.Key == page).Settings.Select(s => s.Key).ToArray();
        Assert.Equal(["looting", "radius", "takeInPlace", "groundLoot", "stones", "oldBooks", "collectibles", "plants",
                      "crops", "fish"], On("looting"));
        Assert.Equal(["corpses", "bosses", "chests", "openDelay", "leaveQuestItems"], On("containers"));
        Assert.Equal(["smash", "smashBuildables", "fellTrees", "smashHives"], On("smashing"));
        Assert.Equal(["itemFilter", "skipList", "onlyList", "skipNotice", "minGearRarity"], On("filters"));
        Assert.Equal(["heightAbove", "heightBelow", "visibleOnly", "fillCarriedStacks", "keepFreeSlots", "carryLimits"], On("where"));
        Assert.Equal(["groundRadius", "collectiblesRadius", "plantsRadius", "cropsRadius", "corpsesRadius", "chestsRadius",
                      "enemyPause", "enemyDistance"], On("ranges"));
        Assert.Equal(["maxStack", "backpackCheat", "enabled", "maxRadius", "allowSmash", "allowFelling", "allowHives"], On("world"));
    }

    [Fact]
    public void Every_setting_is_in_the_window_exactly_once_or_marked_file_only()
    {
        var shown = SettingCatalog.All.Select(s => s.Key).ToList();
        Assert.Equal(shown.Count, shown.Distinct().Count());
        var expected = TextsTests.AllSettingKeys().Except(SettingCatalog.FileOnly).OrderBy(k => k);
        Assert.Equal(expected, shown.OrderBy(k => k));
        Assert.Empty(SettingCatalog.FileOnly.Intersect(shown));
    }

    [Fact]
    public void Owners_match_the_settings_classes()
    {
        static bool Has(Type t, string key) => t.GetProperty(char.ToUpperInvariant(key[0]) + key[1..]) is not null;
        foreach (var s in SettingCatalog.All)
        {
            var type = s.Owner switch
            {
                SettingOwner.Personal => typeof(PersonalSettings),
                SettingOwner.World => typeof(ServerSettings),
                _ => typeof(ServerLimits),
            };
            Assert.True(Has(type, s.Key), s.Key);
        }
        Assert.All(SettingCatalog.Pages.Single(p => p.Key == "world").Settings, s => Assert.NotEqual(SettingOwner.Personal, s.Owner));
    }

    [Fact]
    public void Every_setting_has_a_property_and_only_enabled_needs_a_restart()
    {
        foreach (var s in SettingCatalog.All)
        {
            object holder = s.Owner switch
            {
                SettingOwner.Personal => new PersonalSettings(),
                SettingOwner.World => new ServerSettings(),
                _ => new ServerLimits(),
            };
            Assert.NotNull(SettingValues.Get(holder, s.Key));
        }
        Assert.Equal(["enabled"], SettingCatalog.All.Where(s => s.NeedsRestart).Select(s => s.Key));
        Assert.Equal("limits.maxRadius", SettingCatalog.All.Single(s => s.Key == "maxRadius").Path);
    }
}

public class NoticeTests
{
    [Fact]
    public void Notices_carry_the_mod_name_and_two_lines()
    {
        Assert.Equal("Kiln Auto Loot  Your inventory is full, nothing more is picked up\nCorpses and chests keep their loot until you make room.",
            NoticeText.For(NoticeKind.InventoryFull, null, "F8", false, KilnAutoLoot.Core.Texts.Get));
        Assert.Equal("Kiln Auto Loot  Skipped: Twigs\nOn your skip list, F8 to change it",
            NoticeText.For(NoticeKind.LeftBehind, "Twigs", "F8", false, KilnAutoLoot.Core.Texts.Get));
        Assert.Equal("Kiln Auto Loot  Ausgelassen: Zweige\nSteht auf deiner Filterliste, F8 zum Ändern",
            NoticeText.For(NoticeKind.LeftBehind, "Zweige", "F8", true, KilnAutoLoot.Core.Texts.Get));
        Assert.Null(NoticeText.For(NoticeKind.LeftBehind, null, "F8", false, KilnAutoLoot.Core.Texts.Get));   // no name: nothing shown
    }

    [Fact]
    public void The_same_text_is_not_shown_twice_within_3_s()
    {
        var clock = new FakeClock();
        var book = new ToastBook(clock);
        Assert.True(book.ShouldShow("a"));
        clock.NowMs = 2999;
        Assert.False(book.ShouldShow("a"));
        Assert.True(book.ShouldShow("b"));
        clock.NowMs = 3000;
        Assert.True(book.ShouldShow("a"));
    }
}
