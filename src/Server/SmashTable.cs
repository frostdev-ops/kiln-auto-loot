// SPDX-License-Identifier: MIT
// The templates the mod smashes and their kind, decided from the player's own game data (Core/SmashTargets). Kiln
// 0.2.0-dev.28 holds boot patches on a hosting game, so the templates are not tagged at data load: the kind is looked up
// here by template GUID instead. Empty until the data is read; Fill swaps the whole content at once. No Kiln calls.
namespace KilnAutoLoot.Server;

/// <summary>The smash kind of a template. The switches decide at run time (<see cref="SmashRules"/>).</summary>
internal enum SmashKind : byte
{
    /// <summary>SmashTag: a world-only prop, smashed anywhere outside a save zone.</summary>
    Smash = 1,
    /// <summary>SharedTag: also buildable (an item places it), smashed only away from build zones.</summary>
    Shared,
    /// <summary>SmashTag + StoneTag: a rock boulder, smashed only while stones is on.</summary>
    Stone,
    /// <summary>SharedTag + StoneTag.</summary>
    StoneShared,
    /// <summary>TreeTag: a wood mining node, with fellTrees, away from build zones.</summary>
    Tree,
    /// <summary>A wild bee hive: with smashHives a SmashTag, with only fellTrees a TreeTag.</summary>
    Beehive,
    /// <summary>A wild bee hive that is also buildable: with smashHives a SharedTag, with only fellTrees a TreeTag.</summary>
    BeehiveShared,
}

internal sealed class SmashTable
{
    private volatile Dictionary<Guid, SmashKind> _byTemplate;

    private SmashTable(Dictionary<Guid, SmashKind> byTemplate) => _byTemplate = byTemplate;

    /// <summary>A new table with nothing in it (each Data gets its own, filled once the game data is read).</summary>
    public static SmashTable Empty => new([]);

    public static SmashTable FromKinds(IEnumerable<KeyValuePair<Guid, SmashKind>> kinds)
    {
        var t = Empty;
        t.Fill(kinds);
        return t;
    }

    /// <summary>Replaces the whole content.</summary>
    public void Fill(IEnumerable<KeyValuePair<Guid, SmashKind>> kinds)
    {
        var map = new Dictionary<Guid, SmashKind>();
        foreach (var (g, k) in kinds) map[g] = k;
        _byTemplate = map;
    }

    public IReadOnlyDictionary<Guid, SmashKind> All => _byTemplate;

    public int Count => _byTemplate.Count;

    public bool TryGet(Guid template, out SmashKind kind) => _byTemplate.TryGetValue(template, out kind);

    public int CountOf(SmashKind kind) => _byTemplate.Values.Count(k => k == kind);

    /// <summary>Test fixtures: lines "guid&lt;TAB&gt;kind&lt;TAB&gt;name"; '#' starts a comment line; an unknown kind or a
    /// bad GUID skips the line.</summary>
    public static SmashTable Parse(TextReader text)
    {
        var map = new Dictionary<Guid, SmashKind>();
        for (var line = text.ReadLine(); line is not null; line = text.ReadLine())
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var cols = line.Split('\t');
            if (cols.Length < 2 || !Guid.TryParse(cols[0], out var guid) || ParseKind(cols[1]) is not { } kind) continue;
            map[guid] = kind;
        }
        return new SmashTable(map);
    }

    public static string WordOf(SmashKind kind) => kind switch
    {
        SmashKind.Smash => "smash",
        SmashKind.Shared => "shared",
        SmashKind.Stone => "stone",
        SmashKind.StoneShared => "stone-shared",
        SmashKind.Tree => "tree",
        SmashKind.Beehive => "beehive",
        _ => "beehive-shared",
    };

    public static SmashKind? ParseKind(string text) => text switch
    {
        "smash" => SmashKind.Smash,
        "shared" => SmashKind.Shared,
        "stone" => SmashKind.Stone,
        "stone-shared" => SmashKind.StoneShared,
        "tree" => SmashKind.Tree,
        "beehive" => SmashKind.Beehive,
        "beehive-shared" => SmashKind.BeehiveShared,
        _ => null,
    };
}
