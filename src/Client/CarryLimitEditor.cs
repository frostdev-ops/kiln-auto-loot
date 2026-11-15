// SPDX-License-Identifier: MIT
// The carry-limits editor: one row per limit (x, the item or group, the number), a search box to add an item and a
// group combo to add a group. The rows and the text are CarryLimitRows'; this class only draws and keeps the editor's
// own state (what is typed). A number is saved when its field is left; a field left empty removes its row; 0 = take
// none of it. Render thread.
using System.Globalization;
using Kiln;
using KilnAutoLoot.Core;
using Texts = KilnAutoLoot.Core.Texts;

namespace KilnAutoLoot.Client;

internal sealed class CarryLimitEditor(ItemTable items)
{
    private static readonly Color UnknownRed = new(230, 115, 89);
    private static readonly Color HintGrey = new(150, 150, 150);

    private string _search = "";
    private int _group;
    private int _editing = -1;      // the row whose number field has the focus
    private string _amountText = "";
    private bool _refused;

    public void Reset()
    {
        _search = "";
        _group = 0;
        _editing = -1;
        _refused = false;
    }

    /// <summary>The new text after a change that keeps to the bounds, else null (removing always may).</summary>
    internal string? Bounded(string current, string? next)
    {
        if (next is null || next == current) return null;
        if (SkipListBounds.Accepts(current, next))
        {
            _refused = false;
            return next;
        }
        _refused = true;
        return null;
    }

    /// <summary>Draws the editor; returns the new carryLimits text when the player changed it, else null.</summary>
    public string? Draw(UiDraw ui, float fs, float lineHeight, bool german, string raw, string label, string help)
    {
        string T(string key) => Texts.Get(key, german);
        string? result = null;
        ui.Text(label);
        ui.Tooltip(help);

        // add an item: search box, up to 8 suggestions
        ui.NextItemWidth(fs * 12f);
        ui.InputText("##carrySearch", ref _search, 128);
        var box = ui.LastItem;
        if (_search.Length == 0 && !box.Active && ui.HasLayout)
        {
            var hint = T("carrySearchHint");
            var (_, th) = ui.MeasureText(hint);
            ui.DrawText(UiLayer.Window, box.MinX + fs * 0.5f, box.MinY + (box.Height - th) / 2f, hint, HintGrey);
        }
        ui.Tooltip(help);
        // add a group: combo + button
        ui.SameLine();
        ui.NextItemWidth(fs * 10f);
        var names = Texts.ItemGroups.Select(g => german ? g.German : g.English).ToArray();
        _group = Math.Clamp(_group, 0, names.Length - 1);
        ui.Combo("##carryGroup", ref _group, names);
        var group = Texts.ItemGroups[_group].Key;
        if (group.Length > 0 && !CarryLimitRows.Has(raw, CarryLimitRows.GroupEntry(group)))
        {
            ui.SameLine();
            if (ui.SmallButton(T("carryAddGroup")))
                result = CarryLimitRows.With(raw, CarryLimitRows.GroupEntry(group), CarryLimitRows.DefaultAmount);
        }
        var query = TextRules.Trim(_search);
        if (query.Length >= 3)
        {
            var hits = items.Suggest(query, null, german, max: 8);
            var enter = (box.Active || box.Deactivated) && ui.KeyPressed(UiKey.Enter);
            var pick = enter && hits.Count > 0 ? 0 : -1;
            if (hits.Count > 0 && ui.BeginChild("##carryHits", fs * 26f, hits.Count * lineHeight + fs * 0.5f, UiChildFlags.Border))
            {
                for (var i = 0; i < hits.Count; i++)
                    using (ui.Id(hits[i].Id.ToString(CultureInfo.InvariantCulture)))
                    {
                        if (ui.Selectable(ItemTable.DisplayOf(hits[i], german), false, fs * 14f)) pick = i;
                        ui.SameLine();
                        ui.TextDisabled(hits[i].Internal);
                    }
            }
            if (hits.Count > 0) ui.EndChild();
            if (pick >= 0)
            {
                var row = hits[pick];
                result = CarryLimitRows.With(result ?? raw, CarryLimitRows.ItemEntry(row),
                    row.OriginalMaxStack > 0 ? Math.Max(row.OriginalMaxStack, 1) : CarryLimitRows.DefaultAmount);
                _search = "";
            }
        }

        // the rows
        var rows = CarryLimitRows.Parse(result ?? raw);
        if (rows.Count == 0) ui.TextDisabled(T("carryEmpty"));
        var remove = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            using (ui.Id("limit" + i.ToString(CultureInfo.InvariantCulture)))
            {
                if (ui.SmallButton("x")) remove = i;
                ui.SameLine();
                var r = rows[i];
                if (!r.Understood)
                {
                    ui.Text(UnknownRed, r.Entry);
                    ui.SameLine();
                    ui.TextDisabled(T("carryBad"));
                    continue;
                }
                ui.NextItemWidth(fs * 5f);
                var text = _editing == i ? _amountText : r.Amount.ToString(CultureInfo.InvariantCulture);
                ui.InputText("##amount", ref text, 16);
                var field = ui.LastItem;
                ui.Tooltip(string.Format(T("carryAmountHelp"), CarryLimitRows.MaxAmount));
                if (field.Active)
                {
                    _editing = i;
                    _amountText = text;
                }
                if (field.Deactivated && _editing == i)
                {
                    _editing = -1;
                    if (TextRules.Trim(_amountText).Length == 0) remove = i;   // an emptied number removes the row
                    else if (CarryLimitRows.ParseAmount(_amountText) is { } n && n != r.Amount)
                        result = CarryLimitRows.WithAmount(result ?? raw, i, n);
                }
                ui.SameLine();
                ui.Text(NameOf(r.Entry, german));
                ui.SameLine();
                ui.TextDisabled(r.Entry);
            }
        }
        if (remove >= 0)
        {
            result = CarryLimitRows.WithoutAt(result ?? raw, remove);
            _editing = -1;
        }
        if (rows.Count > 0) ui.TextDisabled(string.Format(T("carryCount"), rows.Count));
        if (_refused) ui.TextDisabled(string.Format(T("listFull"), SkipListBounds.MaxEntries));
        return Bounded(raw, result);
    }

    /// <summary>What a row shows: the item's or group's name in the window's language, else the entry.</summary>
    private string NameOf(string entry, bool german)
    {
        if (entry.StartsWith('#'))
        {
            var g = Texts.ItemGroups.FirstOrDefault(x => x.Key.Length > 0 && TextRules.AsciiEqualsIgnoreCase(x.Key, entry[1..]));
            return g.Key is null ? entry[1..] : string.Format(Texts.Get("groupEntry", german), german ? g.German : g.English);
        }
        var name = entry.StartsWith('=') ? entry[1..] : entry;
        return items.TryGetByInternal(name, out var row) ? ItemTable.DisplayOf(row, german) : name;
    }
}
