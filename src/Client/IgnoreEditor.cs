// SPDX-License-Identifier: MIT
// The item-list editor (the skip list, and the only list with its own texts): search box and group combo on one line,
// the whole-group button, the suggestion box (bullet for chosen items, a click toggles, Enter takes the first, "Only
// this" asks the window to gather nothing but that item), the chosen list with x per entry, "Clear the list" (asks once
// more: "Keep it" takes the place of the first click, the clear goes beside it), "Undo" and the count. Both lists keep
// to the same bounds (SkipListBounds). The rules (matching, chosen, unknown, classify, the new raw value) are
// Core.IgnoreList's; this class only draws and keeps the editor's own state. Render thread.
using Kiln;
using KilnAutoLoot.Core;
using Texts = KilnAutoLoot.Core.Texts;

namespace KilnAutoLoot.Client;

/// <summary>The Texts keys an editor shows for its list.</summary>
internal sealed record ListTexts(string AddGroup, string RemoveGroup, string Empty, string Count)
{
    public static readonly ListTexts Skip = new("ignoreGroup", "pickGroup", "emptyList", "ignoredCount");
    public static readonly ListTexts Only = new("onlyAddGroup", "onlyRemoveGroup", "onlyEmpty", "onlyCount");
}

internal sealed class IgnoreEditor(ItemTable items, ListTexts? texts = null)
{
    private readonly ListTexts _texts = texts ?? ListTexts.Skip;

    /// <summary>The item whose "Only this" was pressed in the last draw, or null; the window takes it (and clears it).</summary>
    public ItemRow? GatherOnly { get; set; }

    private static readonly Color UnknownRed = new(230, 115, 89);
    private static readonly Color HintGrey = new(150, 150, 150);

    private string _search = "";
    private int _group;
    private bool _showList;
    private bool _refused;   // the last change would have made the list longer than the bounds
    private bool _clearAsked;   // "Clear the list" was pressed once
    private readonly ListUndo _undo = new();

    // caches: the list for a raw value, the suggestions for a query; both also keyed on the item table's version, so
    // what was built before the table was filled is built again once it is
    private string? _listRaw;
    private int _listVersion;
    private IgnoreList? _list;
    private (string Query, string Group, bool German, int Version) _hitsKey = ("\0", "", false, -1);
    private IReadOnlyList<ItemRow> _hits = [];
    private bool _namesGerman;
    private IReadOnlyList<string>? _groupNames;

    public void Reset()
    {
        _search = "";
        _group = 0;
        _showList = false;
        _refused = false;
        _clearAsked = false;   // the undo stays: a list cleared by mistake comes back after the window was closed
    }

    /// <summary>The step back the editor offers for <paramref name="raw"/> (tests).</summary>
    internal ListUndo Undo => _undo;

    /// <summary><see cref="Bounded"/>, and the undo learns the change (after an undo nothing is left to undo).
    /// </summary>
    internal string? Finish(string current, string? next, bool undoing)
    {
        var taken = Bounded(current, next);
        if (taken is null) return null;
        if (undoing) _undo.Taken();
        else _undo.Changed(current, taken);
        return taken;
    }

    /// <summary>The change the window makes, or null when it would take the list over the bounds the server takes
    /// (<see cref="SkipListBounds"/>); removing is always allowed.</summary>
    internal string? Bounded(string current, string? next)
    {
        if (next is null) return null;
        if (SkipListBounds.Accepts(current, next))
        {
            _refused = false;
            return next;
        }
        _refused = true;
        return null;
    }

    internal IgnoreList ListFor(string raw)
    {
        var version = items.Version;
        if (_list is null || _listRaw != raw || _listVersion != version)
        {
            _list = new IgnoreList(raw, items);
            _listRaw = raw;
            _listVersion = version;
        }
        return _list;
    }

    private IReadOnlyList<string> GroupNames(bool german)
    {
        if (_groupNames is null || _namesGerman != german)
        {
            _groupNames = Texts.ItemGroups.Select(g => german ? g.German : g.English).ToArray();
            _namesGerman = german;
        }
        return _groupNames;
    }

    internal IReadOnlyList<ItemRow> Hits(string query, string group, bool german)
    {
        var key = (query, group, german, items.Version);
        if (key != _hitsKey)
        {
            _hits = items.Suggest(query, group.Length > 0 ? group : null, german);
            _hitsKey = key;
        }
        return _hits;
    }

    /// <summary>Draws the editor; returns the new raw value when the player changed the list, else null.</summary>
    public string? Draw(UiDraw ui, float fs, float lineHeight, bool german, string raw, string label, string help)
    {
        string T(string key) => Texts.Get(key, german);
        var list = ListFor(raw);
        string? result = null;

        ui.Text(label);
        ui.Tooltip(help);

        // search box + group combo on one line
        ui.NextItemWidth(fs * 12f);
        ui.InputText("##search", ref _search, 128);
        var box = ui.LastItem;
        var searching = box.Active;
        if (searching) _showList = true;
        var enter = (box.Active || box.Deactivated) && ui.KeyPressed(UiKey.Enter);
        if (_search.Length == 0 && !box.Active && ui.HasLayout)
        {
            var hint = T("searchHint");   // Kiln's field has no hint, so it is drawn over it
            var (_, th) = ui.MeasureText(hint);
            ui.DrawText(UiLayer.Window, box.MinX + fs * 0.5f, box.MinY + (box.Height - th) / 2f, hint, HintGrey);
        }
        ui.Tooltip(help);
        ui.SameLine();
        ui.NextItemWidth(fs * 10f);
        var names = GroupNames(german);
        _group = Math.Clamp(_group, 0, names.Count - 1);
        if (ui.Combo("##group", ref _group, names)) _showList = true;
        var category = Texts.ItemGroups[_group].Key;
        if (category.Length > 0)
        {
            ui.SameLine();
            if (ui.SmallButton(list.IsGroupChosen(category) ? T(_texts.RemoveGroup) : T(_texts.AddGroup)))
                result = list.ToggledGroup(category);
        }

        // suggestions: 3+ letters, or a group chosen and the box opened
        var query = TextRules.Trim(_search);
        var listable = query.Length >= 3 || (category.Length > 0 && _showList);
        var hits = listable ? Hits(query, category, german) : [];
        var pick = enter && hits.Count > 0 ? 0 : -1;
        if (hits.Count > 0 && (_showList || query.Length >= 3))
        {
            var rows = Math.Min(hits.Count, 8);
            if (ui.BeginChild("##suggestions", fs * 26f, rows * lineHeight + fs * 0.5f, UiChildFlags.Border))
            {
                for (var i = 0; i < hits.Count; i++)
                {
                    var row = hits[i];
                    using (ui.Id(row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    {
                        var chosen = list.IsChosen(row);
                        // U+2022 bullet (in the font range)
                        if (ui.Selectable((chosen ? "• " : "   ") + ItemTable.DisplayOf(row, german), chosen, fs * 11f)) pick = i;
                        ui.SameLine();
                        if (ui.SmallButton(T("gatherOnlyThis"))) GatherOnly = row;
                        ui.Tooltip(T("gatherOnlyThisHelp"));
                        ui.SameLine();
                        ui.TextDisabled(row.Internal);
                    }
                }
            }
            ui.EndChild();
            ui.TextDisabled(string.Format(T("matches"), hits.Count));
        }
        if (pick >= 0)
        {
            result = ListFor(result ?? raw).Toggled(hits[pick]);
            if (category.Length == 0)
            {
                _search = "";
                _showList = false;
            }
        }
        // Kiln's UI cannot tell a click elsewhere, so the box opened for a group stays
        // until "All groups" is chosen again (a query of 3+ letters shows it in any case)
        if (category.Length == 0 && !searching) _showList = false;

        // the chosen list, scrollable from 8 entries on
        var current = ListFor(result ?? raw);
        var entries = current.Entries;
        if (entries.Count == 0)
        {
            _clearAsked = false;
            ui.TextDisabled(T(_texts.Empty));
            var back = DrawUndo(ui, raw, german, sameLine: false);
            return Finish(raw, back ?? result, undoing: back is not null);
        }
        var shown = Math.Min(entries.Count, 8);
        var remove = -1;
        if (ui.BeginChild("##chosen", fs * 26f, shown * lineHeight + fs * 0.4f))
        {
            for (var i = 0; i < entries.Count; i++)
            {
                using (ui.Id(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    if (ui.SmallButton("x")) remove = i;
                    ui.SameLine();
                    DrawEntry(ui, current.Classify(entries[i]), german);
                }
            }
        }
        ui.EndChild();
        if (remove >= 0) result = current.WithoutAt(remove);
        if (_clearAsked)
        {
            if (ui.SmallButton(T("clearKeep") + "###clear")) _clearAsked = false;
            ui.SameLine();
            using (ui.PushColor(UiColorSlot.Text, UnknownRed))
                if (ui.SmallButton(string.Format(T("clearSure"), entries.Count) + "###clearSure"))
                {
                    _clearAsked = false;
                    result = "";
                }
        }
        else if (ui.SmallButton(T("removeAll") + "###clear")) _clearAsked = true;
        var undo = DrawUndo(ui, raw, german, sameLine: true);
        ui.SameLine();
        ui.TextDisabled(string.Format(T(_texts.Count), entries.Count));
        if (!SkipListBounds.Within(raw))
            ui.TextWrapped(string.Format(T("listTooLong"), SkipListBounds.MaxEntries, SkipListBounds.MaxEntryLength,
                SkipListBounds.MaxNameParts));
        else if (_refused)
            ui.TextDisabled(string.Format(T("listFull"), SkipListBounds.MaxEntries));
        return Finish(raw, undo ?? result, undoing: undo is not null);
    }

    /// <summary>"Undo" while the list is what the editor's last change made; returns the list before it when
    /// pressed.</summary>
    private string? DrawUndo(UiDraw ui, string raw, bool german, bool sameLine)
    {
        if (_undo.For(raw) is not { } before) return null;
        if (sameLine) ui.SameLine();
        var pressed = ui.SmallButton(Texts.Get("undo", german) + "###undo");
        ui.Tooltip(Texts.Get("undoHelp", german));
        return pressed ? before : null;
    }

    private static void DrawEntry(UiDraw ui, IgnoreEntryInfo e, bool german)
    {
        switch (e.Kind)
        {
            case IgnoreEntryKind.Group:
                var g = Texts.ItemGroups.FirstOrDefault(x => x.Key == e.GroupKey);
                ui.Text(string.Format(Texts.Get("groupEntry", german), german ? g.German : g.English));
                ui.SameLine();
                ui.TextDisabled(e.Entry);
                break;
            case IgnoreEntryKind.Unknown:
                ui.Text(UnknownRed, e.Entry);
                ui.SameLine();
                ui.TextDisabled(Texts.Get("unknownEntry", german));
                break;
            case IgnoreEntryKind.Item when e.Item is { } row:
                ui.Text(ItemTable.DisplayOf(row, german));
                ui.SameLine();
                ui.TextDisabled(e.Text);
                break;
            case IgnoreEntryKind.Exact:
                ui.Text(e.Text);
                break;
            default:
                ui.Text(e.Entry);
                ui.SameLine();
                ui.TextDisabled(Texts.Get("namePart", german));
                break;
        }
    }
}

/// <summary>One step back for a list editor: the list as it was before the editor's last change, offered while the list
/// is still what that change made (a profile switch, F9 or a hand edit of the file drops it). It outlives the window
/// being closed and opened again, for this run of the game. Pure.</summary>
internal sealed class ListUndo
{
    private (string Before, string After)? _last;

    public void Changed(string before, string after)
    {
        if (before != after) _last = (before, after);
    }

    /// <summary>The list to go back to while the list is <paramref name="raw"/>, else null.</summary>
    public string? For(string raw) => _last is { } l && l.After == raw ? l.Before : null;

    public void Taken() => _last = null;
}
