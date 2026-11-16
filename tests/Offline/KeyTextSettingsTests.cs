// SPDX-License-Identifier: MIT
// The window and notice texts, and the server's clamp.
using System.Reflection;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class TextsTests
{
    /// <summary>Every settings key: the scalar and enum properties of the three settings classes, camelCase.</summary>
    internal static IReadOnlyList<string> AllSettingKeys() =>
        new[] { typeof(PersonalSettings), typeof(ServerSettings), typeof(ServerLimits) }
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum)
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToList();

    [Theory]
    [InlineData("de", "en-US", true)] [InlineData("en", "de-DE", false)] [InlineData("auto", "de-DE", true)]
    [InlineData("auto", "german", false)] [InlineData("auto", "DE", true)] [InlineData("auto", "en", false)]
    [InlineData("auto", "", false)] [InlineData("xx", "de", true)]
    public void German(string setting, string game, bool expected) => Assert.Equal(expected, Texts.German(setting, game));

    [Fact]
    public void Every_setting_has_a_label_and_help_in_both_languages()
    {
        var keys = AllSettingKeys();
        Assert.Equal(47, keys.Count);
        foreach (var k in keys)
            foreach (var g in new[] { false, true })
            {
                Assert.NotEqual(k, Texts.Label(k, g));
                Assert.NotEqual(k, Texts.Help(k, g));
                Assert.False(string.IsNullOrWhiteSpace(Texts.Label(k, g)), k);
                Assert.True(Texts.Help(k, g).Trim().Length >= 20, $"{k} ({(g ? "de" : "en")}): help too short");
            }
        Assert.Equal(keys.OrderBy(k => k), Texts.Settings.Select(t => t.Key).OrderBy(k => k));   // no texts for a setting that is gone
    }

    [Fact]
    public void German_and_English_differ_and_German_uses_du()
    {
        foreach (var t in Texts.Settings)
            Assert.NotEqual(t.HelpEn, t.HelpDe);
        foreach (var t in Texts.Settings)
            Assert.DoesNotMatch(@"\b(Sie|Ihr|Ihre|Ihnen)\b", t.HelpDe);   // du, not Sie
    }

    [Fact]
    public void Help_says_what_the_mod_does()
    {
        // everything applies at once: only `enabled` speaks of a new start
        foreach (var t in Texts.Settings.Where(t => t.Key != "enabled"))
        {
            Assert.DoesNotContain("starts again", t.HelpEn);
            Assert.DoesNotContain("restart", t.HelpEn, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Neustart", t.HelpDe);
            Assert.DoesNotContain("nächsten Start", t.HelpDe);
        }
        Assert.Contains("once the world is started again", Texts.Help("enabled", false));
        Assert.Contains("nächsten Start der Welt", Texts.Help("enabled", true));
        // smashing never in a base; felled trees and lowered stacks warn
        foreach (var key in new[] { "smash", "smashHives", "fellTrees" })
        {
            Assert.Matches("(?i)never (in|inside)", Texts.Help(key, false));
            Assert.Matches("(?i)nie", Texts.Help(key, true));
        }
        Assert.Contains("Warning: trees cut down this way might never regrow", Texts.Help("fellTrees", false));
        Assert.Contains("Achtung", Texts.Help("fellTrees", true));
        Assert.Contains("Warning: if the mod is removed or this is lowered", Texts.Help("maxStack", false));
        Assert.Contains("Achtung", Texts.Help("maxStack", true));
        Assert.Contains("kiln-auto-loot-server.json", Texts.Get("defaultsNote", false));
    }

    [Fact]
    public void Every_shown_setting_is_on_its_page_and_file_only_ones_are_marked()
    {
        foreach (var page in KilnAutoLoot.Client.SettingCatalog.Pages)
            foreach (var spec in page.Settings)
                Assert.Equal(page.Key, Texts.SectionOf(spec.Key));
        foreach (var key in KilnAutoLoot.Client.SettingCatalog.FileOnly)
            Assert.Equal("file", Texts.SectionOf(key));
    }

    [Fact]
    public void Window_and_notice_texts()
    {
        Assert.Equal("Kiln Auto Loot 0.1.0", Texts.Format("title", false, "0.1.0"));
        Assert.Equal("Kiln Auto Loot 0.1.0", Texts.Format("title", true, "0.1.0"));
        Assert.Equal("Schließen (F8)", Texts.Format("close", true, "F8"));
        Assert.Equal("12 found", Texts.Format("matches", false, 12));
        Assert.Equal("Skipped: Twigs", Texts.Format("leftBehindLine1", false, "Twigs"));
        Assert.Equal("Steht auf deiner Filterliste, F8 zum Ändern", Texts.Format("leftBehindLine2", true, "F8"));
        Assert.Equal("Auto-Loot an", Texts.Get("toggleOn", true));
        Assert.Equal("the server uses 15 m", Texts.Format("serverApplies", false, "15 m"));
        Assert.Equal("F9 switches it", Texts.Format("statusKey", false, "F9"));
        Assert.Equal("no.such.key", Texts.Get("no.such.key", true));
        Assert.Equal("nope", Texts.Label("nope", false));
        Assert.Equal("nope", Texts.Section("nope", false));
        foreach (var k in new[] { "title", "footer", "restartNote", "close", "default", "statusOn", "statusOff", "statusKey",
                     "statusCapped", "statusWaiting", "statusLoading", "hostNote", "defaultsNote", "readOnlyServer",
                     "searchHint", "ignoreGroup", "pickGroup", "matches", "emptyList", "groupEntry", "unknownEntry", "namePart",
                     "removeAll", "ignoredCount", "toggleOn", "toggleOff", "fullLine1", "fullLine2", "leftBehindLine1",
                     "leftBehindLine2", "serverApplies",
                     "onlyAddGroup", "onlyRemoveGroup", "onlyEmpty", "onlyCount", "gatherOnlyThis", "gatherOnlyThisHelp", "listUnused",
                     "noLimit", "zeroOff", "lineOfSightMissing", "carrySearchHint", "carryAddGroup", "carryEmpty", "carryAmountHelp",
                     "carryBad", "carryCount", "profile", "profileNew", "profileNewHelp", "profileRename", "profileRenameHelp",
                     "profileDelete", "profileDeleteSure", "profileDeleteHelp", "profileReset", "profileResetHelp", "profileName",
                     "profileKey", "profileKeyHelp", "profileKeyBad", "profileKeyTaken", "profileSave", "profileCancel",
                     "profileNextKey", "profileNewName", "profileMine", "profileSwitched", "profileSaveError",
                     "focusOn", "focusOff", "focusHint", "focusNoServer", "focusStatus", "focusKey", "focusStop" })
        {
            Assert.NotEqual(k, Texts.Get(k, false));
            Assert.NotEqual(k, Texts.Get(k, true));
            Assert.False(string.IsNullOrWhiteSpace(Texts.Get(k, true)), k);
        }
        foreach (var k in new[] { "looting", "containers", "smashing", "filters", "where", "ranges", "world", "pickUp", "perGroup",
                     "enemies", "limits", "height", "sight", "bag" })
        {
            Assert.NotEqual(k, Texts.Section(k, false));
            Assert.NotEqual(k, Texts.Section(k, true));
        }
    }

    [Fact]
    public void Choices_profiles_and_focus_have_texts_in_both_languages()
    {
        var keys = Enum.GetNames<FilterMode>().Select(v => "itemFilter." + v)
            .Concat(Enum.GetNames<GearRarity>().Select(v => "minGearRarity." + v))
            .Concat(KilnAutoLoot.Client.BuiltInProfiles.Ids.Select(id => "profile." + id));
        foreach (var k in keys)
        {
            Assert.NotEqual(k, Texts.Get(k, false));
            Assert.NotEqual(k, Texts.Get(k, true));
            Assert.NotEqual(Texts.Get(k, false), Texts.Get(k, true));
        }
        Assert.Equal("Gathering only: Twigs", Texts.Format("focusOn", false, "Twigs"));
        Assert.Equal("Sammle nur: Äste", Texts.Format("focusOn", true, "Äste"));
        Assert.Contains("by hand", Texts.Format("focusHint", false, "F11"));
        Assert.Contains("F11", Texts.Format("focusHint", true, "F11"));
        Assert.Contains("can't check what you see", Texts.Get("lineOfSightMissing", false));
        Assert.Contains("0 = no limit", Texts.Help("heightAbove", false));
        Assert.Contains("0 = keine Grenze", Texts.Help("heightBelow", true));
        // the carry-limits help says the window writes internal names, and typed names need a server that knows them
        Assert.Contains("internal names", Texts.Help("carryLimits", false));
        Assert.Contains("server knows", Texts.Help("carryLimits", false));
        Assert.Contains("Server diese Namen kennt", Texts.Help("carryLimits", true));
    }

    [Fact]
    public void Texts_name_only_Kiln_Auto_Loot()
    {
        foreach (var t in Texts.Settings)
            foreach (var s in new[] { t.LabelEn, t.LabelDe, t.HelpEn, t.HelpDe })
                Assert.DoesNotMatch(@"(?<!Kiln )Auto Loot|Auto Loot \p{Lu}|Plus\b", s);
    }

    [Fact]
    public void Item_groups_start_with_every_group_and_have_unique_keys()
    {
        Assert.Equal("", Texts.ItemGroups[0].Key);
        Assert.Equal(Texts.ItemGroups.Count, Texts.ItemGroups.Select(g => g.Key).Distinct().Count());
        foreach (var g in Texts.ItemGroups)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.English));
            Assert.False(string.IsNullOrWhiteSpace(g.German));
        }
    }

    [Fact]
    public void The_combo_has_exactly_the_item_menu_groups_in_order()
    {
        // "" (every group) first, then the menu groups the item rules can give, in the window's order
        Assert.Equal(new[] { "" }.Concat(ItemRules.MenuGroups), Texts.ItemGroups.Select(g => g.Key));
    }
}

public class SettingsRulesTests
{
    [Fact]
    public void Radii_capped_by_the_server_and_zero_stays_zero()
    {
        var s = new PersonalSettings { Radius = 40, GroundRadius = 0, ChestsRadius = 45, PlantsRadius = 10, EnemyDistance = 30 };
        var c = SettingsRules.Clamp(s, new ServerLimits { MaxRadius = 20 });
        Assert.Equal(20f, c.Radius);
        Assert.Equal(0f, c.GroundRadius);
        Assert.Equal(20f, c.ChestsRadius);
        Assert.Equal(10f, c.PlantsRadius);
        Assert.Equal(20f, c.EnemyDistance);
        Assert.Equal(40f, s.Radius);   // a copy
    }

    [Fact]
    public void Ranges_and_switches()
    {
        var s = new PersonalSettings { Radius = 0.2f, OpenDelay = 9, CropsRadius = -4, EnemyDistance = float.NaN,
            Smash = true, FellTrees = true, SmashHives = true, Language = "fr", SkipList = null! };
        var c = SettingsRules.Clamp(s, new ServerLimits { AllowSmash = false, AllowFelling = false, AllowHives = false });
        Assert.Equal(1f, c.Radius);
        Assert.Equal(5f, c.OpenDelay);
        Assert.Equal(0f, c.CropsRadius);
        Assert.Equal(1f, c.EnemyDistance);
        Assert.False(c.Smash);
        Assert.False(c.FellTrees);
        Assert.False(c.SmashHives);
        Assert.Equal("auto", c.Language);
        Assert.Equal("", c.SkipList);
        var open = SettingsRules.Clamp(new PersonalSettings { FellTrees = true, SmashHives = true }, new ServerLimits());
        Assert.True(open.Smash);
        Assert.True(open.FellTrees);
        Assert.True(open.SmashHives);
        Assert.Equal(15f, open.Radius);
    }
}
