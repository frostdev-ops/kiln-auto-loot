// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Per player: the "left behind" counters: at most 3 notices per item per session,
/// counted as notices shown; the same item again only after 10 s. Never reset (also not by the skipNotice switch).</summary>
public sealed class NoticeBook
{
    public const int MaxPerItem = 3;
    public const long SameItemGapMs = 10000;  // the same item's next notice no sooner than 10 s later

    private readonly IClock _clock;
    private readonly Dictionary<uint, (int Count, long Last)> _shown = new();

    public NoticeBook(IClock clock) => _clock = clock;

    /// <summary>True = show "left behind: &lt;item&gt;" now (this counts as one of the item's three).</summary>
    public bool ShouldNotify(uint itemId, bool skipNotice)
    {
        if (!skipNotice) return false;
        long now = _clock.NowMs;
        if (_shown.TryGetValue(itemId, out var c) && (c.Count >= MaxPerItem || now - c.Last < SameItemGapMs)) return false;
        _shown[itemId] = (c.Count + 1, now);
        return true;
    }

    /// <summary>Notices shown for this item so far.</summary>
    public int Shown(uint itemId) => _shown.TryGetValue(itemId, out var c) ? c.Count : 0;
}
