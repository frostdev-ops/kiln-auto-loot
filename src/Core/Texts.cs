// SPDX-License-Identifier: MIT
// Everything the player reads, in English and German (du-form, like the game). Short and plain, and only what this
// mod does: settings apply at once except `enabled`, the server may cap a player's choices, players without the mod
// get the server's defaults.
//
// API:
//   Label(settingKey, german), Help(settingKey, german)  settingKey = a settings key (Core/Settings.cs, camelCase).
//   SectionOf(settingKey)       the page a setting is shown on (Client/SettingCatalog.cs), "file" = only in the file.
//   Section(key, german)        page names (looting containers smashing filters ranges world) and the group headings
//                               inside a page (pickUp perGroup enemies limits).
//   Get(key, german), Format(key, german, args)  the window's and the notices' other strings ({0} = argument).
//   ItemGroups                  the skip-list browser's groups: (Key = ItemRow.MenuGroup, "" = all; English; German).
// An unknown key returns the key itself.
namespace KilnAutoLoot.Core;

/// <summary>The window and notice texts in English and German.</summary>
public static class Texts
{
    /// <summary>One setting's texts. <see cref="Section"/> is the page it is shown on.</summary>
    public sealed record SettingText(string Key, string Section, string LabelEn, string LabelDe, string HelpEn, string HelpDe);

    /// <summary>"de" German, "en" English, otherwise ("auto") German when the game language starts with "de".</summary>
    public static bool German(string languageSetting, string gameLanguage) => languageSetting switch
    {
        "de" => true,
        "en" => false,
        _ => (gameLanguage ?? "").StartsWith("de", StringComparison.OrdinalIgnoreCase),
    };

    public static string Label(string settingKey, bool german) =>
        BySetting.TryGetValue(settingKey, out var t) ? (german ? t.LabelDe : t.LabelEn) : settingKey;

    public static string Help(string settingKey, bool german) =>
        BySetting.TryGetValue(settingKey, out var t) ? (german ? t.HelpDe : t.HelpEn) : settingKey;

    /// <summary>The page a setting is shown on, or "" when unknown.</summary>
    public static string SectionOf(string settingKey) => BySetting.TryGetValue(settingKey, out var t) ? t.Section : "";

    public static string Section(string sectionKey, bool german) =>
        Sections.TryGetValue(sectionKey, out var t) ? (german ? t.De : t.En) : sectionKey;

    public static string Get(string key, bool german) =>
        Strings.TryGetValue(key, out var t) ? (german ? t.De : t.En) : key;

    public static string Format(string key, bool german, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key, german), args);

    /// <summary>The skip-list browser's groups, in order. Key = <see cref="ItemRow.MenuGroup"/>, "" = every group.
    /// The keys follow the item data's categories.</summary>
    public static IReadOnlyList<(string Key, string English, string German)> ItemGroups { get; } = new[]
    {
        ("", "Every group", "Jede Gruppe"),
        ("Materials", "Raw materials", "Rohstoffe"),
        ("Consumables", "Food, drinks and healing", "Essen, Getränke und Heilung"),
        ("AnimalFood", "Animal feed", "Tierfutter"),
        ("Ammunition", "Arrows, bolts, charges and bait", "Pfeile, Bolzen, Ladungen und Köder"),
        ("Weapons", "Weapons", "Waffen"),
        ("Equipment", "Armor, rings and shields", "Rüstung, Ringe und Schilde"),
        ("Tools", "Tools and fishing rods", "Werkzeug und Angeln"),
        ("Instrument", "Music instruments", "Musikinstrumente"),
        ("WeaponGem", "Gems for weapons", "Steine für Waffen"),
        ("Collectibles", "Collectibles", "Sammelobjekte"),
        ("ColorPalette", "Paint colours", "Farbtöne"),
        ("BuildTools", "Found furnishings", "Fundstücke zum Aufstellen"),
    };

    private const string GroupRangeEn = " 0 = the main radius. It may reach further than the main radius.";
    private const string GroupRangeDe = " 0 = der Hauptradius. Darf auch weiter reichen als der Hauptradius.";

    /// <summary>Every setting with texts, page by page; the file-only settings last.</summary>
    public static IReadOnlyList<SettingText> Settings { get; } = new SettingText[]
    {
        // Looting
        new("looting", "looting", "Auto loot", "Auto-Loot",
            "Picks up what is around you as you move, no need to hold E. Off: only what you take yourself.",
            "Sammelt auf, was um dich herum liegt, ganz ohne E zu halten. Aus: nur, was du selbst nimmst."),
        new("radius", "looting", "Radius", "Radius",
            "How far the mod reaches around you, in meters. Smashing uses the same distance. A group can get its own range on the Ranges page.",
            "Wie weit die Mod um dich herum greift, in Metern. Beim Zertrümmern gilt derselbe Abstand. Eigene Reichweiten je Gruppe stellst du unter „Reichweiten“ ein."),
        new("takeInPlace", "looting", "Take where it lies", "Direkt an Ort und Stelle",
            "Things are taken on the spot instead of flying over to you. Off: the game pulls them in, and mushrooms, fireflies and other fixed things are only taken when you stand right next to them.",
            "Dinge landen sofort bei dir, statt erst heranzufliegen. Aus: das Spiel zieht sie heran, und Pilze, Glühwürmchen und andere feste Dinge gibt es nur, wenn du direkt danebenstehst."),
        new("visibleOnly", "looting", "Only what I can see", "Nur, was ich sehe",
            "Leaves out things hidden behind walls, rocks or leaves. Where the server cannot check the view, everything counts as visible.",
            "Lässt aus, was hinter Wänden, Felsen oder Laub versteckt ist. Kann der Server die Sicht nicht prüfen, gilt alles als sichtbar."),
        new("groundLoot", "looting", "Things on the ground", "Dinge am Boden",
            "Loose things out in the world: rocks, sticks, wood, ore, mushrooms, fallen berries, potions, arrows and enemy drops. Off: they stay put, and holding E passes them over too; a single press of E still takes one.",
            "Lose Sachen draußen in der Welt: Steine, Stöcke, Holz, Erz, Pilze, heruntergefallene Beeren, Tränke, Pfeile und Gegner-Drops. Aus: alles bleibt, wo es ist, auch wenn du E hältst; ein einzelner Druck auf E nimmt trotzdem etwas auf."),
        new("stones", "looting", "Stones", "Steine",
            "Stones and stone piles. Switching this off also spares boulders from smashing. Flint does not count as stone. Needs Things on the ground.",
            "Steine und Steinhaufen. Ist das aus, bleiben auch Felsbrocken beim Zertrümmern verschont. Feuerstein zählt nicht dazu. Geht nur mit „Dinge am Boden“."),
        new("collectibles", "looting", "Collectibles", "Sammelobjekte",
            "Finds you keep for their own sake: meteorites, fossils, mosaics, gnomes, urns, paint colours, event items.",
            "Fundstücke, die du einfach sammelst: Meteoriten, Fossilien, Mosaike, Gnome, Urnen, Farbtöne, Event-Sachen."),
        new("plants", "looting", "Plants and bushes", "Pflanzen und Sträucher",
            "What grows on living plants: berries, twigs, herbs, plant fibre, reeds, fireflies. Sticks lying around belong to Things on the ground.",
            "Was an lebenden Pflanzen wächst: Beeren, Zweige, Kräuter, Pflanzenfasern, Schilf, Glühwürmchen. Herumliegende Stöcke gehören zu „Dinge am Boden“."),
        new("crops", "looting", "Ripe crops", "Reife Ernte",
            "Harvests farm beds once they are ripe, in your base too. Seedlings are left to grow.",
            "Erntet deine Beete, sobald sie reif sind, auch in der Basis. Setzlinge dürfen weiterwachsen."),
        new("fish", "looting", "Fish", "Fische",
            "Catches fish swimming within the radius. Off: fish are left alone.",
            "Holt Fische aus dem Wasser im Radius. Aus: sie schwimmen weiter."),

        // Containers
        new("corpses", "containers", "Enemy corpses", "Gegnerleichen",
            "Empties defeated enemies, with the parts and bones they drop.",
            "Leert besiegte Gegner, samt Tierteilen und Knochen, die sie fallen lassen."),
        new("bosses", "containers", "Bosses and elites", "Bosse und Elitegegner",
            "Also empties bosses, mini bosses and elite enemies, bonus loot and all. Off: you loot those yourself with E.",
            "Leert auch Bosse, Minibosse und Elitegegner, Bonusbeute inklusive. Aus: die plünderst du selbst mit E."),
        new("chests", "containers", "Chests and containers", "Truhen und Behälter",
            "Empties chests, coffins, cupboards, shelves and heaps out in the world. Chests you built are never touched.",
            "Leert Truhen, Särge, Schränke, Regale und Haufen draußen in der Welt. Selbst gebaute Truhen rührt die Mod nie an."),
        new("openDelay", "containers", "Wait before opening", "Warten vor dem Öffnen",
            "Seconds the mod waits after it first finds a corpse, chest or plant. Some loot (gems, runes, boss and quest drops) only appears once the game has filled it, so opening too soon can lose it. 0 = straight away, not advised.",
            "So viele Sekunden wartet die Mod, nachdem sie eine Leiche, Truhe oder Pflanze entdeckt hat. Manche Beute (Edelsteine, Runen, Boss- und Questsachen) taucht erst auf, wenn das Spiel fertig befüllt hat; wer zu früh öffnet, verliert sie. 0 = sofort, lieber nicht."),
        new("leaveQuestItems", "containers", "Leave quest items", "Questgegenstände dalassen",
            "The mod leaves quest items in chests and corpses; you take them yourself. Off: they come along with the rest.",
            "Questgegenstände lässt die Mod in Truhen und Leichen, die nimmst du selbst. Aus: sie kommen mit dem Rest mit."),

        // Smashing
        new("smash", "smashing", "Smash things around me", "Zertrümmern",
            "Breaks barrels, crates, baskets, pots, urns and wrecked furniture within the radius, once a second. Never in a base.",
            "Zertrümmert Fässer, Kisten, Körbe, Krüge, Urnen und kaputte Möbel im Radius, einmal pro Sekunde. In einer Basis nie."),
        new("smashBuildables", "smashing", "Also buildable furniture", "Auch baubare Möbel",
            "Also breaks furniture players could build themselves, such as barrels, bales and tables, but only while you are well away from any base.",
            "Zertrümmert auch Möbel, die Spieler selbst bauen können, etwa Fässer, Ballen und Tische, aber nur, wenn du weit genug von jeder Basis weg bist."),
        new("fellTrees", "smashing", "Cut down trees (experimental)", "Bäume umlegen (experimentell)",
            "Experimental. Brings down trees, stumps, logs and dead wood within the radius and picks up the wood, resin and twigs. Warning: trees cut down this way might never regrow, so save a backup first or test it on a spare world. Never in or near a base, never fruit trees such as hazelnut or coffee. Any hive hanging on a tree comes down with it. You get a little less than by chopping. Needs Smash things around me.",
            "Experimentell. Legt Bäume, Stümpfe, Stämme und Totholz im Radius um und sammelt Holz, Harz und Zweige ein. Achtung: Umgelegte Bäume wachsen womöglich nicht nach, also sichere vorher deinen Spielstand oder probier es in einer Testwelt. Nie in oder bei einer Basis, nie Fruchtbäume wie Haselnuss oder Kaffee. Hängt ein Bienenstock am Baum, fällt er mit. Du bekommst etwas weniger als beim Hacken. Geht nur mit „Zertrümmern“."),
        new("smashHives", "smashing", "Bee hives", "Bienenstöcke",
            "Breaks wild bee hives in range and picks up the honey. Some hives let wasps out. Never in a base. Needs Smash things around me.",
            "Zertrümmert wilde Bienenstöcke im Radius und sammelt den Honig ein. Aus manchen kommen Wespen. In einer Basis nie. Geht nur mit „Zertrümmern“."),

        // Filters
        new("skipList", "filters", "Skip these items", "Diese Gegenstände auslassen",
            "Items on this list stay where they are: on the ground, in chests and in corpses. Plants and beds that would give one are not harvested. You can still take them yourself with E. Search by name, or pick a whole group.",
            "Was auf dieser Liste steht, bleibt, wo es ist: am Boden, in Truhen und in Leichen. Pflanzen und Beete, die so etwas liefern, werden nicht geerntet. Mit E kannst du es trotzdem selbst nehmen. Such nach dem Namen oder nimm gleich eine ganze Gruppe."),
        new("skipNotice", "filters", "Note when something is skipped", "Hinweis beim Auslassen",
            "Shows a short note when something from your list is left behind (up to three notes per item each session). Off: no note, the list still works.",
            "Zeigt kurz an, wenn etwas von deiner Liste liegen bleibt, höchstens dreimal pro Gegenstand und Sitzung. Aus: kein Hinweis, die Liste gilt weiter."),

        // Ranges
        new("groundRadius", "ranges", "Things on the ground", "Dinge am Boden",
            "How far the mod reaches for things on the ground, in meters." + GroupRangeEn,
            "So weit greift die Mod nach Dingen am Boden, in Metern." + GroupRangeDe),
        new("collectiblesRadius", "ranges", "Collectibles", "Sammelobjekte",
            "How far the mod reaches for collectibles, in meters." + GroupRangeEn,
            "So weit greift die Mod nach Sammelobjekten, in Metern." + GroupRangeDe),
        new("plantsRadius", "ranges", "Plants and bushes", "Pflanzen und Sträucher",
            "How far the mod reaches for plants and bushes, in meters." + GroupRangeEn,
            "So weit greift die Mod nach Pflanzen und Sträuchern, in Metern." + GroupRangeDe),
        new("cropsRadius", "ranges", "Ripe crops", "Reife Ernte",
            "How far the mod reaches for ripe crops, in meters." + GroupRangeEn,
            "So weit greift die Mod nach reifer Ernte, in Metern." + GroupRangeDe),
        new("corpsesRadius", "ranges", "Enemy corpses", "Gegnerleichen",
            "How far the mod reaches for enemy corpses, in meters." + GroupRangeEn,
            "So weit greift die Mod nach Gegnerleichen, in Metern." + GroupRangeDe),
        new("chestsRadius", "ranges", "Chests and containers", "Truhen und Behälter",
            "How far the mod reaches for chests and containers, in meters." + GroupRangeEn,
            "So weit greift die Mod nach Truhen und Behältern, in Metern." + GroupRangeDe),
        new("enemyPause", "ranges", "Pause near enemies", "Pause bei Gegnern",
            "Stops looting while an enemy is within the distance below, so nothing gets in the way of a fight. Picks up again 2 seconds after the last one is gone.",
            "Hält das Aufsammeln an, solange ein Gegner näher ist als der Abstand darunter, damit dir im Kampf nichts dazwischenkommt. Ist der letzte Gegner zwei Sekunden weg, sammelt die Mod weiter."),
        new("enemyDistance", "ranges", "Enemy distance", "Gegnerabstand",
            "How close an enemy has to be, in meters, to pause looting.",
            "Wie nah ein Gegner sein muss, in Metern, damit das Aufsammeln pausiert."),

        // World
        new("maxStack", "world", "Stack size", "Stapelgröße",
            "Largest stack for stackable items, up to 65535. Game default = the game's own sizes. Warning: if the mod is removed or this is lowered, the game cuts bigger stacks down to its size when it next loads them, and the extra items are gone.",
            "Größter Stapel für stapelbare Gegenstände, bis 65535. „Wie im Spiel“ = die Größen des Spiels. Achtung: Wird die Mod entfernt oder der Wert gesenkt, kürzt das Spiel größere Stapel beim nächsten Laden auf seine Größe, und der Überschuss ist weg."),
        new("backpackCheat", "world", "Backpack cheat", "Rucksack-Cheat",
            "Cheat: crafting any backpack gives you the top one, and each ingredient costs just one.",
            "Cheat: Egal welchen Rucksack du baust, du bekommst den stärksten, und jede Zutat kostet nur ein Stück."),
        new("enabled", "world", "Mod on in this world", "Mod in dieser Welt an",
            "Switches all of Kiln Auto Loot on or off for this world. This one only applies once the world is started again.",
            "Schaltet Kiln Auto Loot für diese Welt ganz an oder aus. Das greift erst beim nächsten Start der Welt."),
        new("maxRadius", "world", "Largest radius", "Größter Radius",
            "No player's radius, group range or enemy distance goes beyond this, in meters.",
            "Weiter als das reicht bei keinem Spieler ein Radius, eine Gruppen-Reichweite oder der Gegnerabstand, in Metern."),
        new("allowSmash", "world", "Smashing allowed", "Zertrümmern erlaubt",
            "Off: nothing is smashed for anyone, whatever they chose.",
            "Aus: für niemanden wird etwas zertrümmert, egal was eingestellt ist."),
        new("allowFelling", "world", "Tree cutting allowed", "Bäume umlegen erlaubt",
            "Off: no trees come down for anyone, whatever they chose.",
            "Aus: für niemanden werden Bäume umgelegt, egal was eingestellt ist."),
        new("allowHives", "world", "Bee hives allowed", "Bienenstöcke erlaubt",
            "Off: no bee hives are smashed for anyone, whatever they chose.",
            "Aus: für niemanden werden Bienenstöcke zertrümmert, egal was eingestellt ist."),

        // Only in the files
        new("language", "file", "Language", "Sprache",
            "Language of this window and the notes: auto, en or de. auto follows the game.",
            "Sprache dieses Fensters und der Hinweise: auto, en oder de. auto richtet sich nach dem Spiel."),
        new("counterLog", "file", "Counter log", "Zähler-Log",
            "Writes the mod's counters to kiln-auto-loot.log every 5 seconds, for finding problems.",
            "Schreibt alle 5 Sekunden die Zähler der Mod in kiln-auto-loot.log, zur Fehlersuche."),
    };

    private static readonly Dictionary<string, SettingText> BySetting = Settings.ToDictionary(t => t.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, (string En, string De)> Sections = new(StringComparer.Ordinal)
    {
        // pages
        ["looting"] = ("Looting", "Aufsammeln"),
        ["containers"] = ("Containers", "Behälter"),
        ["smashing"] = ("Smashing", "Zertrümmern"),
        ["filters"] = ("Filters", "Filter"),
        ["ranges"] = ("Ranges", "Reichweiten"),
        ["world"] = ("World", "Welt"),
        // headings inside a page
        ["pickUp"] = ("What to pick up", "Was mitkommt"),
        ["perGroup"] = ("Range per group", "Reichweite je Gruppe"),
        ["enemies"] = ("Enemies nearby", "Gegner in der Nähe"),
        ["limits"] = ("Limits for players", "Grenzen für Spieler"),
    };

    private static readonly Dictionary<string, (string En, string De)> Strings = new(StringComparer.Ordinal)
    {
        // the window
        ["title"] = ("Kiln Auto Loot {0}", "Kiln Auto Loot {0}"),
        ["footer"] = ("Every change is saved straight away.", "Jede Änderung wird gleich gespeichert."),
        ["restartNote"] = ("* applies when the world starts again", "* greift erst beim nächsten Start der Welt"),
        ["close"] = ("Close ({0})", "Schließen ({0})"),
        ["default"] = ("Game default", "Wie im Spiel"),
        // the status line
        ["statusOn"] = ("Auto loot is on", "Auto-Loot ist an"),
        ["statusOff"] = ("Auto loot is off", "Auto-Loot ist aus"),
        ["statusKey"] = ("{0} switches it", "{0} schaltet um"),
        ["statusCapped"] = ("The server caps {0} of your settings (shown below them)", "Der Server begrenzt {0} deiner Einstellungen (steht jeweils darunter)"),
        ["statusWaiting"] = ("No answer from the server yet. Without Kiln Auto Loot there, nothing is looted.", "Noch keine Antwort vom Server. Läuft Kiln Auto Loot dort nicht, wird nichts aufgesammelt."),
        ["statusLoading"] = ("The item list is still loading.", "Die Gegenstandsliste lädt noch."),
        // the World page
        ["hostNote"] = ("You host this world: these settings count for everyone in it.", "Du hostest diese Welt: diese Einstellungen gelten für alle darin."),
        ["defaultsNote"] = ("Players without the mod get the defaults from kiln-auto-loot-server.json.", "Spieler ohne die Mod bekommen die Vorgaben aus kiln-auto-loot-server.json."),
        ["readOnlyServer"] = ("Set by the server you play on.", "Legt der Server fest, auf dem du spielst."),
        // the skip-list editor
        ["searchHint"] = ("Search items (3+ letters)", "Gegenstand suchen (ab 3 Zeichen)"),
        ["ignoreGroup"] = ("Skip this group", "Gruppe auslassen"),
        ["pickGroup"] = ("Take this group again", "Gruppe wieder mitnehmen"),
        ["matches"] = ("{0} found", "{0} gefunden"),
        ["emptyList"] = ("Nothing skipped: the mod takes everything.", "Nichts ausgelassen: die Mod nimmt alles mit."),
        ["groupEntry"] = ("Group: {0}", "Gruppe: {0}"),
        ["unknownEntry"] = ("(no item by this name)", "(kein Gegenstand mit diesem Namen)"),
        ["namePart"] = ("(every name containing this)", "(alles mit diesem Namensteil)"),
        ["removeAll"] = ("Clear the list", "Liste leeren"),
        ["ignoredCount"] = ("{0} on the list", "{0} auf der Liste"),
        ["listFull"] = ("The list is full: at most {0} entries.", "Die Liste ist voll: höchstens {0} Einträge."),
        ["listTooLong"] = ("This list is too long for the server (at most {0} entries of {1} characters, {2} of them name parts): it keeps your last list that fit.",
            "Diese Liste ist dem Server zu lang (höchstens {0} Einträge mit je {1} Zeichen, davon {2} Namensteile): er behält deine letzte passende Liste."),
        // the notices
        ["toggleOn"] = ("Auto loot on", "Auto-Loot an"),
        ["toggleOff"] = ("Auto loot off: take things with E", "Auto-Loot aus: Sachen mit E nehmen"),
        ["fullLine1"] = ("Your inventory is full, nothing more is picked up", "Dein Inventar ist voll, es kommt nichts mehr dazu"),
        ["fullLine2"] = ("Corpses and chests keep their loot until you make room.", "Leichen und Truhen behalten ihre Beute, bis du Platz schaffst."),
        ["leftBehindLine1"] = ("Skipped: {0}", "Ausgelassen: {0}"),
        ["leftBehindLine2"] = ("On your skip list, {0} to change it", "Steht auf deiner Filterliste, {0} zum Ändern"),
        // the server's answer, shown in the window
        ["serverApplies"] = ("the server uses {0}", "der Server nimmt {0}"),
    };
}
