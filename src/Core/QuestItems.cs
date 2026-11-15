// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>The items that stay in containers with leaveQuestItems on (<see cref="ItemRules.IsQuestItem"/> decides them
/// from the player's own game data). Empty until that is done. <see cref="Fill"/> swaps the whole set at once.</summary>
public sealed class QuestItems
{
    private volatile HashSet<uint> _ids = new();

    private QuestItems() { }

    public static QuestItems Empty() => new();

    public static QuestItems FromIds(IEnumerable<uint> ids)
    {
        var q = new QuestItems();
        q.Fill(ids);
        return q;
    }

    public void Fill(IEnumerable<uint> ids) => _ids = new HashSet<uint>(ids);

    /// <summary>Test fixtures: every line that starts with a digit gives a decimal item id.</summary>
    public static QuestItems Parse(TextReader text)
    {
        var ids = new List<uint>();
        foreach (var line in TextRules.Lines(text))
            if (line.Length > 0 && line[0] >= '0' && line[0] <= '9') ids.Add(TextRules.ParseUInt(line));
        return FromIds(ids);
    }

    /// <summary>Distinct ids.</summary>
    public int Count => _ids.Count;

    public IEnumerable<uint> All => _ids;

    public bool Contains(uint itemId) => _ids.Contains(itemId);
}
