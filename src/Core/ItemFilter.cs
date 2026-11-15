// SPDX-License-Identifier: MIT
// Which items one player lets the mod take, as one decision over the item id:
//   - gather focus on: that one item and nothing else (the skip list and the only list do not matter while it is on);
//   - itemFilter only: the items of the only list and nothing else;
//   - itemFilter skip: everything except the skip list.
namespace KilnAutoLoot.Core;

/// <summary>Why an item is refused by the filter.</summary>
public enum ItemRefusal
{
    None,
    /// <summary>On the skip list (skip mode): the "left behind" notice may follow.</summary>
    Skipped,
    /// <summary>Not on the only list (only mode).</summary>
    NotListed,
    /// <summary>Gather focus is on another item.</summary>
    NotFocus,
}

public sealed class ItemFilter
{
    public ItemFilter(FilterMode mode, IgnoreList skip, IgnoreList? only, uint focus)
    {
        Mode = mode;
        Skip = skip;
        Only = only;
        Focus = focus;
    }

    public FilterMode Mode { get; }
    public IgnoreList Skip { get; }
    public IgnoreList? Only { get; }

    /// <summary>The gather focus item, 0 = off.</summary>
    public uint Focus { get; }

    /// <summary>Only listed items are taken (only mode or gather focus): everything else stays without a notice.</summary>
    public bool Picks => Focus != 0 || Mode == FilterMode.Only;

    /// <summary>The refusal a picking filter gives an item it does not list.</summary>
    public ItemRefusal Unlisted => Focus != 0 ? ItemRefusal.NotFocus : ItemRefusal.NotListed;

    public ItemRefusal Check(uint item)
    {
        if (Focus != 0) return item == Focus ? ItemRefusal.None : ItemRefusal.NotFocus;
        if (Mode == FilterMode.Only) return Only is not null && Only.Contains(item) ? ItemRefusal.None : ItemRefusal.NotListed;
        return Skip.Contains(item) ? ItemRefusal.Skipped : ItemRefusal.None;
    }

    public bool Allows(uint item) => Check(item) == ItemRefusal.None;

    /// <summary>A skip-mode filter over a skip list (no only list, no focus).</summary>
    public static ItemFilter SkipOnly(IgnoreList skip) => new(FilterMode.Skip, skip, null, 0);
}
