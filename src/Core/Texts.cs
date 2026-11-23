// SPDX-License-Identifier: MIT
// Everything the player reads, in English and German (du-form, like the game). Short and plain, and only what this
// mod does: settings apply at once except `enabled`, the server may cap a player's choices, players without the mod
// get the server's defaults.
//
// API:
//   Label(settingKey, german), Help(settingKey, german)  settingKey = a settings key (Core/Settings.cs, camelCase).
//   SectionOf(settingKey)       the page a setting is shown on (Client/SettingCatalog.cs), "file" = only in the file.
//   Section(key, german)        page names (looting containers smashing filters where ranges world) and the group
//                               headings inside a page (pickUp perGroup enemies limits height sight bag).
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
            "Picks up what is around you as you move, no need to hold E. Off: only what you take yourself, and nothing is smashed.",
            "Sammelt auf, was um dich herum liegt, ganz ohne E zu halten. Aus: nur, was du selbst nimmst, und nichts wird zertrümmert."),
        new("radius", "looting", "Radius", "Radius",
            "How far the mod reaches around you, in meters. Smashing uses the same distance. A group can get its own range on the Ranges page.",
            "Wie weit die Mod um dich herum greift, in Metern. Beim Zertrümmern gilt derselbe Abstand. Eigene Reichweiten je Gruppe stellst du unter „Reichweiten“ ein."),
        new("takeInPlace", "looting", "Take where it lies", "Direkt an Ort und Stelle",
            "Things are taken on the spot instead of flying over to you. Off: the game pulls them in, and mushrooms and other fixed things are only taken when you stand right next to them.",
            "Dinge landen sofort bei dir, statt erst heranzufliegen. Aus: das Spiel zieht sie heran, und Pilze und andere feste Dinge gibt es nur, wenn du direkt danebenstehst."),
        new("visibleOnly", "where", "Only what I can see", "Nur, was ich sehe",
            "Leaves out things hidden behind walls, rocks or leaves. Where the server cannot check the view, everything counts as visible.",
            "Lässt aus, was hinter Wänden, Felsen oder Laub versteckt ist. Kann der Server die Sicht nicht prüfen, gilt alles als sichtbar."),
        new("groundLoot", "looting", "Things on the ground", "Dinge am Boden",
            "Loose things out in the world: rocks, sticks, wood, ore, mushrooms, fallen berries, potions, arrows and enemy drops. Off: the mod leaves them and boulders stay whole; you can still take them yourself with E.",
            "Lose Sachen draußen in der Welt: Steine, Stöcke, Holz, Erz, Pilze, heruntergefallene Beeren, Tränke, Pfeile und Gegner-Drops. Aus: die Mod lässt alles liegen, und Felsbrocken bleiben ganz; mit E kannst du es trotzdem selbst nehmen."),
        new("stones", "looting", "Stones", "Steine",
            "Stones and stone piles. Switching this off also spares boulders and stone carts from smashing. Flint does not count as stone. Needs Things on the ground.",
            "Steine und Steinhaufen. Ist das aus, bleiben auch Felsbrocken und Steinkarren beim Zertrümmern verschont. Feuerstein zählt nicht dazu. Geht nur mit „Dinge am Boden“."),
        new("oldBooks", "looting", "Old books", "Alte Bücher",
            "Books lying in villages and ruins, which give Old Books. Off by default, as they are often part of the décor. Even when on, they stay while you are in or near a base; you can always take them yourself with E. Needs Things on the ground.",
            "Bücher, die in Dörfern und Ruinen liegen und Alte Bücher geben. Standardmäßig ausgeschaltet, weil sie oft zur Deko gehören. Auch eingeschaltet bleiben sie liegen, solange du in oder nahe einer Basis bist; mit E kannst du sie immer selbst nehmen. Geht nur mit „Dinge am Boden“."),
        new("collectibles", "looting", "Collectibles", "Sammelobjekte",
            "Finds you keep for their own sake: meteorites, fossils, mosaics, gnomes, urns, paint colours, event items.",
            "Fundstücke, die du einfach sammelst: Meteoriten, Fossilien, Mosaike, Gnome, Urnen, Farbtöne, Event-Sachen."),
        new("plants", "looting", "Plants and bushes", "Pflanzen und Sträucher",
            "What grows on living plants: berries, twigs, herbs, plant fibre, reeds. Sticks lying around belong to Things on the ground.",
            "Was an lebenden Pflanzen wächst: Beeren, Zweige, Kräuter, Pflanzenfasern, Schilf. Herumliegende Stöcke gehören zu „Dinge am Boden“."),
        new("critters", "looting", "Critters (fishing bait)", "Krabbeltiere (Angelköder)",
            "The little swarms you catch as fishing bait: fireflies, moths, grasshoppers, scarabs, pillbugs, frogs and maggots. Never animals, pets or anything a quest needs. They use the main radius.",
            "Die kleinen Schwärme, die du als Angelköder fängst: Glühwürmchen, Motten, Heuschrecken, Skarabäen, Asseln, Frösche und Maden. Nie Tiere, Haustiere oder etwas, das eine Quest braucht. Es gilt der Hauptradius."),
        new("crops", "looting", "Ripe crops in the world", "Reife Ernte in der Welt",
            "Harvests ripe farm beds out in the world, away from any base. Seedlings are left to grow.",
            "Erntet reife Beete draußen in der Welt, abseits jeder Basis. Setzlinge dürfen weiterwachsen."),
        new("cropsInBase", "looting", "Ripe crops in a base", "Reife Ernte in der Basis",
            "Harvests ripe farm beds while you are in or near a base, and everything you can plant there (algae, wheat, pumpkins, bushes). Off: what grows in your base stays as it is, for decoration or to pick by hand with E.",
            "Erntet reife Beete, solange du in oder nahe einer Basis bist, und alles, was man dort pflanzen kann (Algen, Weizen, Kürbisse, Büsche). Aus: was in deiner Basis wächst, bleibt stehen, als Deko oder zum Ernten von Hand mit E."),
        new("animalProduce", "looting", "Farm animal produce", "Erzeugnisse der Nutztiere",
            "Collects what your grown farm animals have ready (milk from goats, wool from yaks, ...), as Collect would, with the game's own pickup note. Babies are never grown up and animals never petted. Uses the main radius. Off by default.",
            "Sammelt, was deine ausgewachsenen Nutztiere bereithalten (Milch von Ziegen, Wolle von Yaks, ...), wie Einsammeln, mit dem Hinweis des Spiels. Jungtiere werden nie großgezogen und Tiere nie gestreichelt. Es gilt der Hauptradius. Standardmäßig aus."),
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
        new("itemFilter", "filters", "Which list decides", "Welche Liste entscheidet",
            "Skip: everything is taken except what is on the skip list. Only: nothing is taken except what is on the only list. Both lists stay saved, so you can switch back and forth.",
            "Auslassen: alles kommt mit, außer was auf der Auslassliste steht. Nur: nichts kommt mit, außer was auf der Nur-Liste steht. Beide Listen bleiben gespeichert, du kannst also hin und her wechseln."),
        new("onlyList", "filters", "Take only these items", "Nur diese Gegenstände nehmen",
            "With Only chosen above, the mod takes just these items from the ground, chests and corpses. A plant or crop is harvested whole as soon as anything it gives is on this list, so the rest of what it gives comes along too; with a bag rule on (fill carried stacks, free slots, carry limits) the whole harvest has to fit. You can still take anything yourself with E. Search by name, or pick a whole group.",
            "Ist oben „Nur“ gewählt, nimmt die Mod vom Boden, aus Truhen und Leichen nur diese Gegenstände. Eine Pflanze oder ein Beet wird ganz geerntet, sobald etwas davon auf dieser Liste steht; was sie sonst noch gibt, kommt also mit. Ist eine Rucksack-Regel an (Stapel auffüllen, Plätze frei halten, Höchstmengen), muss die ganze Ernte passen. Mit E kannst du weiterhin alles selbst nehmen. Such nach dem Namen oder nimm gleich eine ganze Gruppe."),
        new("minGearRarity", "filters", "Gear from this rarity on", "Ausrüstung ab dieser Seltenheit",
            "Weapons, armor, rings and shields below this rarity stay where they are, so your bag does not fill up with common gear. Common = all gear is taken. Other items are not affected.",
            "Waffen, Rüstung, Ringe und Schilde unter dieser Seltenheit bleiben liegen, damit sich dein Rucksack nicht mit gewöhnlichem Zeug füllt. Gewöhnlich = alles kommt mit. Andere Gegenstände betrifft das nicht."),
        new("skipNotice", "filters", "Note when something is skipped", "Hinweis beim Auslassen",
            "Shows a short note when something from your list is left behind (up to three notes per item each session). Off: no note, the list still works.",
            "Zeigt kurz an, wenn etwas von deiner Liste liegen bleibt, höchstens dreimal pro Gegenstand und Sitzung. Aus: kein Hinweis, die Liste gilt weiter."),

        // Where and the bag
        new("heightAbove", "where", "Height above me", "Höhe über mir",
            "Only takes things at most this many meters above your feet, so loot on the floor above you stays there. Fishing bait (critters) is taken within your main radius instead. 0 = no limit.",
            "Nimmt nur, was höchstens so viele Meter über deinen Füßen liegt; Beute im Stockwerk über dir bleibt also dort. Angelköder (Krabbeltiere) nimmt es stattdessen im Hauptradius. 0 = keine Grenze."),
        new("heightBelow", "where", "Depth below me", "Tiefe unter mir",
            "Only takes things at most this many meters below your feet, so a cave under you keeps its loot until you go down. Fishing bait (critters) is taken within your main radius instead. 0 = no limit.",
            "Nimmt nur, was höchstens so viele Meter unter deinen Füßen liegt; eine Höhle unter dir behält ihre Beute also, bis du hinuntergehst. Angelköder (Krabbeltiere) nimmt es stattdessen im Hauptradius. 0 = keine Grenze."),
        new("fillCarriedStacks", "where", "Only fill stacks I carry", "Nur getragene Stapel auffüllen",
            "Only takes items you already have in your bag, and only until that stack is full. Nothing new starts a stack. Handy with a few sticks in the bag: you gather sticks until the stack is full, and nothing else.",
            "Nimmt nur Gegenstände, die du schon im Rucksack hast, und nur, bis der Stapel voll ist. Nichts Neues fängt einen Stapel an. Praktisch mit ein paar Stöcken im Rucksack: du sammelst Stöcke, bis der Stapel voll ist, und sonst nichts."),
        new("keepFreeSlots", "where", "Keep bag slots free", "Rucksackplätze frei halten",
            "Once only this many bag slots are empty, the mod only tops up stacks you already carry and leaves the free slots for you. 0 = off.",
            "Sind nur noch so viele Rucksackplätze leer, füllt die Mod nur noch Stapel auf, die du schon trägst, und lässt dir die freien Plätze. 0 = aus."),
        new("carryLimits", "where", "Carry at most", "Höchstens tragen",
            "Stops taking an item once you carry this many of it; a group limit counts every item of the group together. The window writes the game's internal names (=Material_T1_Twigs:500, #Materials:2000). English or German names typed into the file by hand only work where the server knows those names.",
            "Hört auf, einen Gegenstand zu nehmen, sobald du so viele davon trägst; eine Gruppengrenze zählt alle Gegenstände der Gruppe zusammen. Das Fenster schreibt die internen Namen des Spiels (=Material_T1_Twigs:500, #Materials:2000). Englische oder deutsche Namen, die du selbst in die Datei schreibst, wirken nur, wo der Server diese Namen kennt."),

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
            "How far the mod reaches for ripe crops, in the world and in a base, in meters." + GroupRangeEn,
            "So weit greift die Mod nach reifer Ernte, in der Welt und in der Basis, in Metern." + GroupRangeDe),
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
        new("allowSalvage", "world", "Salvage allowed", "Verwerten erlaubt",
            "Off: nobody can salvage gear through the mod, and salvage rules do nothing. The game's own Trash still works.",
            "Aus: niemand kann über die Mod Ausrüstung verwerten, und Verwertungsregeln tun nichts. Das Wegwerfen im Spiel geht weiter."),

        // Salvage
        new("salvageConfirm", "salvage", "Ask before salvaging", "Vor dem Verwerten fragen",
            "Shows how many items, how many runes and which ones, before the marked gear is salvaged. Off: the button salvages at once.",
            "Zeigt vor dem Verwerten, wie viele Gegenstände, wie viele Runen und welche. Aus: der Knopf verwertet sofort."),
        new("salvageRuleAction", "salvage", "What a rule does", "Was eine Regel tut",
            "Mark: new gear a rule matches is marked, and you salvage it here when you like. Salvage: it is turned into runes as soon as it is in your backpack, with no question.",
            "Markieren: neue Ausrüstung, auf die eine Regel passt, wird markiert, und du verwertest sie hier, wann du willst. Verwerten: sie wird sofort zu Runen, sobald sie im Rucksack ist, ohne Nachfrage."),
        new("salvageRules", "salvage", "Rules for new gear", "Regeln für neue Ausrüstung",
            "Only gear you pick up from the world after a rule is on: loot from corpses, world chests and the ground. Never what you already carry, what you take from your own chests, a tombstone, crafted or worn gear, kept or quest items, or gear with a gem in it. No rules by default.",
            "Nur Ausrüstung, die du aufhebst, nachdem eine Regel an ist: Beute von Leichen, Truhen in der Welt und vom Boden. Nie, was du schon trägst, was du aus deinen eigenen Truhen nimmst, ein Grabstein, Hergestelltes oder Angelegtes, behaltene oder Quest-Gegenstände oder Ausrüstung mit Edelstein. Standardmäßig gibt es keine Regeln."),

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
        ["where"] = ("Where & bag", "Ort & Rucksack"),
        ["ranges"] = ("Ranges", "Reichweiten"),
        ["world"] = ("World", "Welt"),
        // headings inside a page
        ["pickUp"] = ("What to pick up", "Was mitkommt"),
        ["perGroup"] = ("Range per group", "Reichweite je Gruppe"),
        ["enemies"] = ("Enemies nearby", "Gegner in der Nähe"),
        ["limits"] = ("Limits for players", "Grenzen für Spieler"),
        ["salvage"] = ("Salvage", "Verwerten"),
        ["salvageRulesHeading"] = ("Rules for new gear", "Regeln für neue Ausrüstung"),
        ["height"] = ("How high and how deep", "Wie hoch und wie tief"),
        ["sight"] = ("What you can see", "Was du siehst"),
        ["bag"] = ("Your bag", "Dein Rucksack"),
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
        ["menuNote"] = ("No world is running: these settings are saved for the worlds you host and count from their " +
                        "next start, so a save loads with the stack size you set here.",
                        "Keine Welt läuft: Diese Einstellungen gelten für Welten, die du hostest, ab ihrem nächsten " +
                        "Start; ein Spielstand lädt also mit der Stapelgröße, die du hier einstellst."),
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
        ["clearKeep"] = ("Keep it", "Behalten"),
        ["clearSure"] = ("Really clear all {0}?", "Wirklich alle {0} entfernen?"),
        ["undo"] = ("Undo", "Rückgängig"),
        ["undoHelp"] = ("Puts the list back as it was before your last change here.", "Stellt die Liste wieder her, wie sie vor deiner letzten Änderung hier war."),
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
        // the only list (the same editor as the skip list)
        ["onlyAddGroup"] = ("Take this group", "Diese Gruppe nehmen"),
        ["onlyRemoveGroup"] = ("Remove this group", "Gruppe entfernen"),
        ["onlyEmpty"] = ("The only list is empty: with Only chosen, nothing is taken.", "Die Nur-Liste ist leer: mit „Nur“ kommt nichts mit."),
        ["onlyCount"] = ("{0} on the only list", "{0} auf der Nur-Liste"),
        ["gatherOnlyThis"] = ("Only this", "Nur das"),
        ["gatherOnlyThisHelp"] = ("Switch to Only and take nothing but this item.", "Auf „Nur“ wechseln und nichts außer diesem Gegenstand nehmen."),
        ["listUnused"] = ("{0} (not used while the other list decides)", "{0} (gilt gerade nicht, die andere Liste entscheidet)"),
        // the filter switch and the rarity choice
        ["itemFilter.Skip"] = ("Skip: take everything but the skip list", "Auslassen: alles außer der Auslassliste"),
        ["itemFilter.Only"] = ("Only: take just the only list", "Nur: ausschließlich die Nur-Liste"),
        ["minGearRarity.Common"] = ("Common (all gear)", "Gewöhnlich (alle Ausrüstung)"),
        ["minGearRarity.Uncommon"] = ("Uncommon", "Ungewöhnlich"),
        ["minGearRarity.Rare"] = ("Rare", "Selten"),
        ["minGearRarity.Epic"] = ("Epic", "Episch"),
        ["minGearRarity.Legendary"] = ("Legendary", "Legendär"),
        // where and the bag
        ["noLimit"] = ("no limit", "keine Grenze"),
        ["zeroOff"] = ("off", "aus"),
        ["lineOfSightMissing"] = ("This game or server can't check what you see yet, so this has no effect for now.",
            "Dieses Spiel oder dieser Server kann noch nicht prüfen, was du siehst; das wirkt also vorerst nicht."),
        // the carry-limits editor
        ["carrySearchHint"] = ("Add an item (3+ letters)", "Gegenstand hinzufügen (ab 3 Zeichen)"),
        ["carryAddGroup"] = ("Add a limit for this group", "Grenze für diese Gruppe"),
        ["carryEmpty"] = ("No limits: the mod takes as much as fits.", "Keine Grenzen: die Mod nimmt, so viel reinpasst."),
        ["carryAmountHelp"] = ("Carry at most this many (0 to {0}; 0 = take none of it; empty = remove the limit).",
            "Höchstens so viele tragen (0 bis {0}; 0 = nichts davon nehmen; leer = Grenze entfernen)."),
        ["carryBad"] = ("(not understood, write =Name:500)", "(unverständlich, schreib =Name:500)"),
        ["carryCount"] = ("{0} limits", "{0} Grenzen"),
        // the profile bar
        ["profile"] = ("Profile", "Profil"),
        ["profileNew"] = ("New", "Neu"),
        ["profileNewHelp"] = ("A new profile with the settings you have now.", "Ein neues Profil mit deinen jetzigen Einstellungen."),
        ["profileRename"] = ("Rename", "Umbenennen"),
        ["profileRenameHelp"] = ("Change this profile's name and its own key.", "Namen und eigene Taste dieses Profils ändern."),
        ["profileDelete"] = ("Delete", "Löschen"),
        ["profileDeleteSure"] = ("Really delete?", "Wirklich löschen?"),
        ["profileDeleteHelp"] = ("Deletes this profile. The last one cannot be deleted.", "Löscht dieses Profil. Das letzte bleibt immer."),
        ["profileReset"] = ("Reset", "Zurücksetzen"),
        ["profileResetHelp"] = ("Puts this built-in profile back as it came.", "Setzt dieses mitgelieferte Profil auf den Anfang zurück."),
        ["profileName"] = ("Name", "Name"),
        ["profileKey"] = ("Key", "Taste"),
        ["profileKeyHelp"] = ("A key that switches straight to this profile, such as Ctrl+1. Empty = none. You can change it later in Kiln's Keys page too.",
            "Eine Taste, die gleich zu diesem Profil wechselt, etwa Ctrl+1. Leer = keine. Ändern kannst du sie auch später auf Kilns Tastenseite."),
        ["profileKeyBad"] = ("Kiln does not know this key.", "Diese Taste kennt Kiln nicht."),
        ["profileKeyTaken"] = ("This key already belongs to {0}.", "Diese Taste gehört schon zu {0}."),
        ["profileSave"] = ("Save", "Speichern"),
        ["profileCancel"] = ("Cancel", "Abbrechen"),
        ["profileNextKey"] = ("{0} switches to the next profile", "{0} wechselt zum nächsten Profil"),
        ["profileNextNoKey"] = ("Give \"Next profile\" a key in Kiln's Keys page to switch with one press",
            "Gib \"Next profile\" auf Kilns Tastenseite eine Taste, dann wechselst du mit einem Druck"),
        ["profileNewName"] = ("Profile {0}", "Profil {0}"),
        ["profileMine"] = ("My settings", "Meine Einstellungen"),
        ["profileSwitched"] = ("Profile: {0}", "Profil: {0}"),
        ["profileOutOfRange"] = ("This profile was not switched to: one of its settings is out of range (check profiles.json).",
            "Nicht gewechselt: eine Einstellung dieses Profils liegt außerhalb des erlaubten Bereichs (sieh in profiles.json nach)."),
        ["profileListTooLong"] = ("This profile was not switched to: one of its lists is too long.",
            "Nicht gewechselt: eine Liste dieses Profils ist zu lang."),
        ["profileTooLarge"] = ("This profile was not switched to: its settings are too large to send to the server.",
            "Nicht gewechselt: die Einstellungen dieses Profils sind zu groß, um sie an den Server zu schicken."),
        ["profileWriteFailed"] = ("This profile was not switched to, your settings stay as they were: {0}",
            "Nicht gewechselt, deine Einstellungen bleiben, wie sie waren: {0}"),
        ["settingsTooLarge"] = ("Not saved: all your lists together would be too large to send to the server. Shorten one of them.",
            "Nicht gespeichert: alle deine Listen zusammen wären zu groß für den Server. Kürze eine davon."),
        ["profileSaveError"] = ("The profiles could not be saved: {0}", "Die Profile konnten nicht gespeichert werden: {0}"),
        // the built-in profiles
        ["profile.everything"] = ("Everything", "Alles"),
        ["profile.wood"] = ("Wood & sticks", "Holz & Stöcke"),
        ["profile.stone"] = ("Stone & ore", "Stein & Erz"),
        ["profile.plants"] = ("Plants & crops", "Pflanzen & Ernte"),
        ["profile.plunder"] = ("Plunder", "Plündern"),
        ["profile.treasure"] = ("Treasure hunter", "Schatzsuche"),
        // gather focus
        ["focusOn"] = ("Gathering only: {0}", "Sammle nur: {0}"),
        ["focusOff"] = ("Gathering everything again", "Sammle wieder alles"),
        ["focusHint"] = ("Nothing to focus on yet: take one item by hand first, then press {0}.",
            "Noch nichts zum Festlegen: nimm erst einen Gegenstand selbst auf, dann {0} drücken."),
        ["focusNoServer"] = ("The server does not run Kiln Auto Loot, so there is nothing to focus.", "Auf dem Server läuft Kiln Auto Loot nicht, es gibt nichts festzulegen."),
        ["focusStatus"] = ("Gathering only: {0} ({1} for everything again)", "Sammle nur: {0} ({1} für wieder alles)"),
        ["focusKey"] = ("{0} gathers only the item you last took by hand", "{0} sammelt nur, was du zuletzt selbst genommen hast"),
        ["focusHelp"] = ("Gather focus: the mod takes only that one item from the ground, chests and corpses. A plant or crop that gives it is harvested whole, so the rest of what it gives comes along too; with a bag rule on the whole harvest has to fit. You can still take anything yourself with E. Press the key again for everything.",
            "Sammelfokus: die Mod nimmt vom Boden, aus Truhen und Leichen nur noch diesen einen Gegenstand. Eine Pflanze oder ein Beet, das ihn gibt, wird ganz geerntet; was es sonst noch gibt, kommt also mit. Ist eine Rucksack-Regel an, muss die ganze Ernte passen. Mit E kannst du weiterhin alles selbst nehmen. Die Taste noch einmal: wieder alles."),
        ["focusStop"] = ("Gather everything", "Alles sammeln"),
        // the server's answer, shown in the window
        ["serverApplies"] = ("the server uses {0}", "der Server nimmt {0}"),
        // salvage
        ["salvageRuleAction.Mark"] = ("Mark it", "Markieren"),
        ["salvageRuleAction.Salvage"] = ("Salvage it at once", "Sofort verwerten"),
        ["salvageIntro"] = ("Turn gear into runes, with the game's own Trash yield. Mark what can go, then salvage it in one go. Worn gear, kept and quest items and gear with a gem are never salvaged.",
            "Mach aus Ausrüstung Runen, so viele wie beim Wegwerfen im Spiel. Markiere, was weg kann, und verwerte alles auf einmal. Angelegtes, Behaltenes, Quest-Gegenstände und Ausrüstung mit Edelstein werden nie verwertet."),
        ["salvageNoServer"] = ("The server does not run this version of Kiln Auto Loot, so there is no salvage here.", "Auf dem Server läuft diese Version von Kiln Auto Loot nicht, hier gibt es kein Verwerten."),
        ["salvageWaiting"] = ("Asking the server for your gear...", "Frage den Server nach deiner Ausrüstung ..."),
        ["salvageOff"] = ("The server has salvage switched off.", "Der Server hat das Verwerten ausgeschaltet."),
        ["salvageNoItemData"] = ("Salvage needs Kiln's item data, which the server does not have: update Kiln there.", "Verwerten braucht Kilns Gegenstandsdaten, die der Server nicht hat: Kiln dort aktualisieren."),
        ["salvageNoGameData"] = ("The game's rune numbers could not be read, so nothing is salvaged.", "Die Runenwerte des Spiels ließen sich nicht lesen, darum wird nichts verwertet."),
        ["salvageMismatch"] = ("Off in this world: the game gave other runes than the mod expected (see kiln.log). Please report it.", "In dieser Welt aus: das Spiel gab andere Runen als die Mod erwartet hat (siehe kiln.log). Bitte melde das."),
        ["salvageRefresh"] = ("Refresh", "Neu laden"),
        ["salvageStarting"] = ("The world just loaded: salvage starts in a few seconds.", "Die Welt ist gerade geladen: Verwerten geht in ein paar Sekunden los."),
        ["salvageGear"] = ("Gear in your backpack: {0}", "Ausrüstung im Rucksack: {0}"),
        ["salvageNoGear"] = ("No gear in your backpack.", "Keine Ausrüstung im Rucksack."),
        ["salvageOnlyMarked"] = ("Only marked", "Nur markierte"),
        ["salvageLevel"] = ("level {0}", "Stufe {0}"),
        ["salvageRunes"] = ("{0} runes", "{0} Runen"),
        ["salvageMark"] = ("Mark", "Markieren"),
        ["salvageUnmark"] = ("Unmark", "Nicht mehr"),
        ["salvageKeep"] = ("Keep", "Behalten"),
        ["salvageUnkeep"] = ("Kept", "Behalten"),
        ["salvageKeepHelp"] = ("Kept gear is never salvaged, by rules neither. Click again to stop keeping it.", "Behaltene Ausrüstung wird nie verwertet, auch nicht durch Regeln. Noch einmal klicken, um sie freizugeben."),
        ["salvageMarkedButton"] = ("Salvage marked ({0}, {1} runes)", "Markierte verwerten ({0}, {1} Runen)"),
        ["salvageClear"] = ("Clear marks", "Markierungen weg"),
        ["salvageMarkAll"] = ("Mark all ({0})", "Alle markieren ({0})"),
        ["salvageMarkAllHelp"] = ("Marks every piece of gear listed that may be salvaged: never kept, worn or quest items, nor gear with a gem. Nothing is salvaged until you confirm.",
            "Markiert jede aufgeführte Ausrüstung, die verwertet werden darf: nie Behaltenes, Angelegtes, Quest-Gegenstände oder Ausrüstung mit Edelstein. Verwertet wird erst, wenn du bestätigst."),
        ["salvageConfirmTitle"] = ("Salvage {0} items for {1} runes?", "{0} Gegenstände für {1} Runen verwerten?"),
        ["salvageConfirmNote"] = ("This cannot be undone: the gear is gone.", "Das lässt sich nicht rückgängig machen: die Ausrüstung ist weg."),
        ["salvageConfirmYes"] = ("Yes, salvage", "Ja, verwerten"),
        ["salvageConfirmNo"] = ("No", "Nein"),
        ["salvageBusy"] = ("Salvaging...", "Verwerte ..."),
        ["salvageDone"] = ("Salvaged {0}: +{1} runes", "Verwertet: {0}, +{1} Runen"),
        ["salvageSkipped"] = ("{0} skipped ({1})", "{0} übersprungen ({1})"),
        ["salvageOwed"] = ("{0} runes did not fit yet: make room in your backpack and they follow.", "{0} Runen passten noch nicht: mach Platz im Rucksack, dann kommen sie nach."),
        ["salvageMarkedToast"] = ("Marked for salvage: {0}", "Zum Verwerten markiert: {0}"),
        ["salvageRulesEmpty"] = ("No rules: new gear is left alone.", "Keine Regeln: neue Ausrüstung bleibt, wie sie ist."),
        ["salvageAddRule"] = ("Add a rule", "Regel hinzufügen"),
        ["salvageUpTo"] = ("up to", "bis"),
        ["salvageMaxLevel"] = ("up to level (0 = any)", "bis Stufe (0 = alle)"),
        ["salvageRuleOn"] = ("on", "an"),
        ["salvageKind.All"] = ("All gear", "Alle Ausrüstung"),
        ["salvageKind.Weapons"] = ("Weapons", "Waffen"),
        ["salvageKind.Shields"] = ("Shields", "Schilde"),
        ["salvageKind.Armor"] = ("Armor", "Rüstung"),
        ["salvageKind.Rings"] = ("Rings", "Ringe"),
        ["rarity.Common"] = ("Common", "Gewöhnlich"),
        ["rarity.Uncommon"] = ("Uncommon", "Ungewöhnlich"),
        ["rarity.Rare"] = ("Rare", "Selten"),
        ["rarity.Epic"] = ("Epic", "Episch"),
        ["rarity.Legendary"] = ("Legendary", "Legendär"),
        ["salvageBlock.None"] = ("", ""),
        ["salvageBlock.Equipped"] = ("worn", "angelegt"),
        ["salvageBlock.Kept"] = ("kept", "behalten"),
        ["salvageBlock.Quest"] = ("quest item", "Quest-Gegenstand"),
        ["salvageBlock.Gem"] = ("has a gem", "hat einen Edelstein"),
        ["salvageBlock.NoPerks"] = ("cannot be salvaged", "nicht verwertbar"),
        ["salvageBlock.Elsewhere"] = ("not in your backpack", "nicht im Rucksack"),
        ["salvageBlock.NoYield"] = ("gives no runes", "gibt keine Runen"),
        ["salvageBlock.Changed"] = ("moved or changed", "verschoben oder verändert"),
        ["salvageBlock.NoRoom"] = ("no room for the runes", "kein Platz für die Runen"),
        ["salvageBlock.NoAccount"] = ("the server cannot tell who you are yet", "der Server weiß noch nicht, wer du bist"),
    };
}
