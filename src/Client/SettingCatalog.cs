// SPDX-License-Identifier: MIT
// What the F8 window shows: one tab per page, each page a list of groups (a group may have a heading), each group a
// list of settings with their widget kind and range. Pure data, no Kiln calls.
namespace KilnAutoLoot.Client;

/// <summary>Choice: a combo over an enum's values (texts "key.Value"). Count: a whole-number slider. CarryLimits: the
/// carry-limit rows editor.</summary>
internal enum WidgetKind { Bool, Meters, Seconds, Stack, ItemList, Choice, Count, CarryLimits, SalvageRules }

/// <summary>Where a setting lives: the player's own file, the world's file (ServerSettings), or the world's limits
/// (ServerSettings.Limits).</summary>
internal enum SettingOwner { Personal, World, Limit }

/// <param name="Key">The settings key (also the Kiln config path inside its file, camelCase).</param>
/// <param name="ZeroText">a Texts.Get key the slider shows at 0 instead of the number ("no limit"), or null.</param>
internal sealed record SettingSpec(string Key, WidgetKind Kind, SettingOwner Owner, float Min = 0, float Max = 0,
    string? ZeroText = null)
{
    /// <summary>The Kiln config path in the owner's file ("limits.maxRadius" for a limit).</summary>
    public string Path => Owner == SettingOwner.Limit ? "limits." + Key : Key;

    /// <summary>The `*` restart marker: only `enabled` needs a restart; everything else applies at once.</summary>
    public bool NeedsRestart => Key == "enabled";

    /// <summary>The `*`: the saved value differs from the one the running world started with (null = no world runs).</summary>
    public static bool RestartPending(bool saved, bool? running) => running is { } r && r != saved;
}

/// <param name="Heading">a Texts.Section key drawn above the group, or null for none.</param>
internal sealed record GroupSpec(string? Heading, IReadOnlyList<SettingSpec> Settings);

/// <param name="Key">the page's Texts.Section key (its tab).</param>
internal sealed record PageSpec(string Key, IReadOnlyList<GroupSpec> Groups)
{
    public IEnumerable<SettingSpec> Settings => Groups.SelectMany(g => g.Settings);
}

internal static class SettingCatalog
{
    private static SettingSpec B(string key, SettingOwner owner = SettingOwner.Personal) => new(key, WidgetKind.Bool, owner);
    private static SettingSpec M(string key, float min, float max, SettingOwner owner = SettingOwner.Personal) =>
        new(key, WidgetKind.Meters, owner, min, max);

    private static SettingSpec C(string key) => new(key, WidgetKind.Choice, SettingOwner.Personal);

    /// <summary>Settings kept out of the window: they live only in the files.</summary>
    public static readonly IReadOnlyList<string> FileOnly = ["language", "counterLog", "settingsVersion"];

    public static readonly IReadOnlyList<PageSpec> Pages =
    [
        new("looting", [
            new(null, [B("looting"), M("radius", 1, 50), B("takeInPlace")]),
            new("pickUp", [B("groundLoot"), B("stones"), B("oldBooks"), B("collectibles"), B("plants"), B("critters"), B("crops"), B("cropsInBase"), B("animalProduce"), B("fish")]),
        ]),
        new("containers", [
            new(null, [
                B("corpses"), B("bosses"), B("chests"),
                new("openDelay", WidgetKind.Seconds, SettingOwner.Personal, 0, 5), B("leaveQuestItems"),
            ]),
        ]),
        new("smashing", [
            new(null, [B("smash"), B("smashBuildables"), B("fellTrees"), B("smashHives")]),
        ]),
        // the switch first; then the list that decides, the other one folded away (SettingsWindow draws them in that order)
        new("filters", [
            new(null, [
                C("itemFilter"), new("skipList", WidgetKind.ItemList, SettingOwner.Personal),
                new("onlyList", WidgetKind.ItemList, SettingOwner.Personal), B("skipNotice"), C("minGearRarity"),
            ]),
        ]),
        new("where", [
            new("height", [
                new("heightAbove", WidgetKind.Meters, SettingOwner.Personal, 0, 50, "noLimit"),
                new("heightBelow", WidgetKind.Meters, SettingOwner.Personal, 0, 50, "noLimit"),
            ]),
            new("sight", [B("visibleOnly")]),
            new("bag", [
                B("fillCarriedStacks"), new("keepFreeSlots", WidgetKind.Count, SettingOwner.Personal, 0, 64, "zeroOff"),
                new("carryLimits", WidgetKind.CarryLimits, SettingOwner.Personal),
            ]),
        ]),
        new("ranges", [
            new("perGroup", [
                M("groundRadius", 0, 50), M("collectiblesRadius", 0, 50), M("plantsRadius", 0, 50), M("cropsRadius", 0, 50),
                M("corpsesRadius", 0, 50), M("chestsRadius", 0, 50),
            ]),
            new("enemies", [B("enemyPause"), M("enemyDistance", 1, 50)]),
        ]),
        // the gear list and its buttons come first (SettingsWindow draws SalvagePanel at the page's top)
        new("salvage", [
            new(null, [B("salvageConfirm")]),
            new("salvageRulesHeading", [C("salvageRuleAction"), new("salvageRules", WidgetKind.SalvageRules, SettingOwner.Personal)]),
        ]),
        // the world's owner sets these when hosting; a joined player reads what the server says
        new("world", [
            new(null, [
                new("maxStack", WidgetKind.Stack, SettingOwner.World, 0, 65535), B("backpackCheat", SettingOwner.World),
                B("enabled", SettingOwner.World),
            ]),
            new("limits", [
                M("maxRadius", 1, 50, SettingOwner.Limit), B("allowSmash", SettingOwner.Limit),
                B("allowFelling", SettingOwner.Limit), B("allowHives", SettingOwner.Limit), B("allowSalvage", SettingOwner.Limit),
            ]),
        ]),
    ];

    public static IEnumerable<SettingSpec> All => Pages.SelectMany(p => p.Settings);
}
