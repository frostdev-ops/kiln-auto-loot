// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>The templates the mod may take, each with its group, flags and yield (<see cref="TemplateRules"/> decides
/// them from the player's own game data). Empty until that is done: a template that is not here is never taken.
/// <see cref="Fill"/> swaps the whole content at once.</summary>
public sealed class TemplateTable
{
    private sealed class State
    {
        public readonly List<TemplateRule> All = new();
        public readonly Dictionary<Guid, TemplateRule> ByGuid = new();
        public readonly HashSet<Guid> WorldLootOnly = new();
    }

    private volatile State _state = new();

    private TemplateTable() { }

    public static TemplateTable Empty() => new();

    public static TemplateTable FromRules(IEnumerable<TemplateRule> rules)
    {
        var t = new TemplateTable();
        t.Fill(rules);
        return t;
    }

    /// <summary>Replaces the whole content, with the world loot the mod leaves (salvage only).</summary>
    public void Fill(IEnumerable<TemplateRule> rules, IEnumerable<Guid> worldLootOnly)
    {
        var s = new State();
        foreach (var g in worldLootOnly) s.WorldLootOnly.Add(g);
        Fill(rules, s);
    }

    /// <summary>Replaces the whole content; the first rule of a GUID wins.</summary>
    public void Fill(IEnumerable<TemplateRule> rules) => Fill(rules, new State());

    private void Fill(IEnumerable<TemplateRule> rules, State s)
    {
        foreach (var r in rules)
        {
            s.All.Add(r);
            s.ByGuid.TryAdd(r.Guid, r);
        }
        _state = s;
    }

    /// <summary>A small tab-separated text form for test fixtures: <c>guid, group word, flag words, name, yield ids
    /// (comma separated)</c>; '#' and empty lines are skipped, and so is a line without a GUID.</summary>
    public static TemplateTable Parse(TextReader text)
    {
        var rules = new List<TemplateRule>();
        foreach (var line in TextRules.Lines(text))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var cols = TextRules.Columns(TextRules.Trim(line), 5);
            if (!TextRules.TryParseGuid(TextRules.Trim(cols[0]), out var guid)) continue;
            var flagText = TextRules.Trim(cols[2]);
            var flags = TemplateFlags.None;
            if (flagText.Contains("stone", StringComparison.Ordinal)) flags |= TemplateFlags.Stone;
            if (flagText.Contains("swarm", StringComparison.Ordinal)) flags |= TemplateFlags.Swarm;
            if (flagText.Contains("boss", StringComparison.Ordinal)) flags |= TemplateFlags.Boss;
            if (flagText.Contains("elite", StringComparison.Ordinal)) flags |= TemplateFlags.Elite;
            if (flagText.Contains("animal", StringComparison.Ordinal)) flags |= TemplateFlags.Animal;
            if (flagText.Contains("drop", StringComparison.Ordinal)) flags |= TemplateFlags.Drop;
            if (flagText.Contains("generic", StringComparison.Ordinal)) flags |= TemplateFlags.Generic;
            if (flagText.Contains("decor", StringComparison.Ordinal)) flags |= TemplateFlags.Decor;
            if (flagText.Contains("grown", StringComparison.Ordinal)) flags |= TemplateFlags.Grown;
            var yield = new List<uint>();
            foreach (var part in cols[4].Split(','))
            {
                var id = TextRules.Trim(part);
                if (id.Length > 0) yield.Add(TextRules.ParseUInt(id));
            }
            rules.Add(new TemplateRule(guid, GroupOf(TextRules.Trim(cols[1])), flags, TextRules.Trim(cols[3]), yield));
        }
        return FromRules(rules);
    }

    public int Count => _state.All.Count;

    /// <summary>Every rule, in the order they were given.</summary>
    public IReadOnlyList<TemplateRule> All => _state.All;

    public bool TryGet(Guid template, out TemplateRule rule) => _state.ByGuid.TryGetValue(template, out rule!);

    /// <summary>World loot for the salvage rules: a template the mod takes, or world loot it leaves to the player (a locked
    /// chest, a camp's heap: <see cref="TemplateRules.IsWorldLootOnly"/>).</summary>
    public bool IsWorldLoot(Guid template)
    {
        var s = _state;
        return s.ByGuid.ContainsKey(template) || s.WorldLootOnly.Contains(template);
    }

    /// <summary>A group word as <see cref="WordOf"/> writes it, case-sensitive; any other word is <see cref="Group.Unknown"/>.</summary>
    public static Group GroupOf(string word) => word switch
    {
        "ground" => Group.Ground,
        "collectible" => Group.Collectible,
        "plant" => Group.Plant,
        "farm" => Group.Farm,
        "corpse" => Group.Corpse,
        "chest" => Group.Chest,
        "boss" => Group.Boss,
        "enemy" => Group.Enemy,
        "fish" => Group.Fish,
        "critter" => Group.Critter,
        _ => Group.Unknown,
    };

    /// <summary>The group's word in the debug dump and in test fixtures.</summary>
    public static string WordOf(Group g) => g switch
    {
        Group.Ground => "ground",
        Group.Collectible => "collectible",
        Group.Plant => "plant",
        Group.Farm => "farm",
        Group.Corpse => "corpse",
        Group.Chest => "chest",
        Group.Boss => "boss",
        Group.Enemy => "enemy",
        Group.Fish => "fish",
        Group.Critter => "critter",
        _ => "unknown",
    };
}
