// SPDX-License-Identifier: MIT
// The Salvage tab's own part: the backpack's gear as the server lists it (marked first, marked rows in red), Mark and
// Keep per row, "Salvage marked" with its confirm (count, runes, the list) and "Clear marks", the last result and the
// runes still owed. Asks the server for the list when the tab shows and every 3 s while it does. Render thread; the
// state comes from the server's answer (any thread).
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using Texts = KilnAutoLoot.Core.Texts;

namespace KilnAutoLoot.Client;

internal sealed class SalvagePanel(Data data, Action<SalvageCmd, ulong[]> send, Func<bool> serverHasSalvage)
{
    private static readonly Color MarkedRed = new(235, 95, 85);
    private static readonly Color Muted = new(160, 160, 160);
    private static readonly Color Orange = new(255, 166, 77);
    private static readonly Color Green = new(140, 220, 150);

    private volatile SalvageStateMsg? _state;
    private long _nextAskMs;
    private bool _onlyMarked;
    private SalvageRow[]? _confirm;   // the rows the confirm shows (and salvages)

    public void SetState(SalvageStateMsg m) => _state = m;

    public void Forget()
    {
        _state = null;
        _confirm = null;
    }

    /// <summary>The key "Salvage marked": the next draw shows the confirm for the marked rows.</summary>
    public volatile bool ConfirmAsked;

    private void Ask(SalvageCmd kind, ulong[]? tokens = null)
    {
        send(kind, tokens ?? []);
        _nextAskMs = Environment.TickCount64 + 3000;
    }

    public void Draw(UiDraw ui, float fs, bool german, bool confirmSetting)
    {
        string T(string k) => Texts.Get(k, german);
        ui.TextWrapped(T("salvageIntro"));
        if (!serverHasSalvage())
        {
            ui.Text(Orange, T("salvageNoServer"));
            return;
        }
        if (Environment.TickCount64 >= _nextAskMs) Ask(SalvageCmd.List);
        var s = _state;
        if (s is null)
        {
            ui.TextDisabled(T("salvageWaiting"));
            return;
        }
        var why = s.Ready switch
        {
            SalvageReady.Off => "salvageOff",
            SalvageReady.NoItemData => "salvageNoItemData",
            SalvageReady.NoGameData => "salvageNoGameData",
            SalvageReady.Mismatch => "salvageMismatch",
            SalvageReady.Starting => "salvageStarting",
            _ => null,
        };
        if (why is not null)
        {
            ui.TextWrapped(T(why));
            if (s.Ready != SalvageReady.Mismatch) return;
        }
        if (s.Busy) ui.Text(Orange, T("salvageBusy"));
        if (s.Last.Salvaged > 0 || s.Last.Skipped > 0)
        {
            if (s.Last.Salvaged > 0) ui.Text(Green, Texts.Format("salvageDone", german, s.Last.Salvaged, s.Last.Runes));
            if (s.Last.Skipped > 0)
                ui.TextDisabled(Texts.Format("salvageSkipped", german, s.Last.Skipped, T("salvageBlock." + s.Last.Reason)));
        }
        if (s.OwedRunes > 0) ui.Text(Orange, Texts.Format("salvageOwed", german, s.OwedRunes));

        var marked = s.Rows.Where(r => (r.Flags & SalvageRowFlags.Marked) != 0 && r.Block == SalvageBlock.None).ToArray();
        var markedRunes = marked.Sum(r => r.Runes);
        var ready = s.Ready == SalvageReady.Ready && !s.Busy;

        if (ConfirmAsked)
        {
            ConfirmAsked = false;
            if (marked.Length > 0 && ready) _confirm = marked;
        }
        if (_confirm is { } c)
        {
            DrawConfirm(ui, fs, german, c);
            return;
        }

        ui.Spacing();
        if (ready && marked.Length > 0)
        {
            if (ui.Button(Texts.Format("salvageMarkedButton", german, marked.Length, markedRunes) + "###salvageMarked"))
            {
                if (confirmSetting) _confirm = marked;
                else Ask(SalvageCmd.Salvage, marked.Select(r => r.Token).ToArray());
            }
            ui.SameLine();
            if (ui.Button(T("salvageClear") + "###salvageClear")) Ask(SalvageCmd.ClearMarks);
            ui.SameLine();
        }
        var markable = s.Rows.Where(r => r.Block == SalvageBlock.None && (r.Flags & SalvageRowFlags.Marked) == 0)
            .Select(r => r.Token).ToArray();
        if (ready && markable.Length > 0)
        {
            if (ui.SmallButton(Texts.Format("salvageMarkAll", german, markable.Length) + "###salvageMarkAll"))
                Ask(SalvageCmd.Mark, markable);
            ui.Tooltip(T("salvageMarkAllHelp"));
            ui.SameLine();
        }
        if (ui.SmallButton(T("salvageRefresh") + "###salvageRefresh")) Ask(SalvageCmd.List);
        ui.SameLine();
        ui.Checkbox(T("salvageOnlyMarked") + "###onlyMarked", ref _onlyMarked);

        ui.Separator(Texts.Format("salvageGear", german, s.Rows.Length));
        var rows = _onlyMarked ? s.Rows.Where(r => (r.Flags & SalvageRowFlags.Marked) != 0).ToArray() : s.Rows;
        if (rows.Length == 0)
        {
            ui.TextDisabled(T("salvageNoGear"));
            return;
        }
        ui.BeginChild("gear", fs * 29f, fs * Math.Min(16f, 1.9f * rows.Length + 1f));
        foreach (var r in rows)
            using (ui.Id(r.Token.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                DrawRow(ui, german, r, ready);
        ui.EndChild();
    }

    private string Line(SalvageRow r, bool german)
    {
        var name = data.Items.TryGet(r.Item, out var row) ? ItemTable.DisplayOf(row, german) : "#" + r.Item;
        var rarity = r.Rarity <= 4 ? Texts.Get("rarity." + (GearRarity)r.Rarity, german) : "";
        return $"{name}  ({rarity}, {Texts.Format("salvageLevel", german, r.Level)})";
    }

    /// <summary>One row: the buttons first (a long name never pushes them out of the list), then the name, rarity and
    /// level, then the runes or why it cannot be salvaged.</summary>
    private void DrawRow(UiDraw ui, bool german, SalvageRow r, bool ready)
    {
        var markedRow = (r.Flags & SalvageRowFlags.Marked) != 0;
        var kept = (r.Flags & SalvageRowFlags.Kept) != 0;
        if (ready)
        {
            if (r.Block == SalvageBlock.None)
            {
                if (ui.SmallButton(Texts.Get(markedRow ? "salvageUnmark" : "salvageMark", german) + "###m"))
                    Ask(markedRow ? SalvageCmd.Unmark : SalvageCmd.Mark, [r.Token]);
                ui.SameLine();
            }
            if (r.Block is SalvageBlock.None or SalvageBlock.Kept)
            {
                if (ui.SmallButton(Texts.Get(kept ? "salvageUnkeep" : "salvageKeep", german) + "###k"))
                    Ask(kept ? SalvageCmd.Unkeep : SalvageCmd.Keep, [r.Token]);
                ui.Tooltip(Texts.Get("salvageKeepHelp", german));
                ui.SameLine();
            }
        }
        var line = Line(r, german);
        if (markedRow) ui.Text(MarkedRed, line);
        else if (r.Block != SalvageBlock.None) ui.Text(Muted, line);
        else ui.Text(line);
        ui.SameLine();
        if (r.Block is SalvageBlock.None or SalvageBlock.Kept)
            ui.TextDisabled(Texts.Format("salvageRunes", german, r.Runes));
        else ui.TextDisabled(Texts.Get("salvageBlock." + r.Block, german));
    }

    private void DrawConfirm(UiDraw ui, float fs, bool german, SalvageRow[] rows)
    {
        ui.Separator();
        ui.Text(MarkedRed, Texts.Format("salvageConfirmTitle", german, rows.Length, rows.Sum(r => r.Runes)));
        ui.TextWrapped(Texts.Get("salvageConfirmNote", german));
        ui.BeginChild("confirm", fs * 29f, fs * Math.Min(12f, 1.5f * rows.Length + 1f));
        foreach (var r in rows) ui.Text(Line(r, german) + "  " + Texts.Format("salvageRunes", german, r.Runes));
        ui.EndChild();
        // No first, where the salvage button was: a stray click does not salvage
        if (ui.Button(Texts.Get("salvageConfirmNo", german) + "###no")) _confirm = null;
        ui.SameLine();
        if (ui.Button(Texts.Get("salvageConfirmYes", german) + "###yes"))
        {
            _confirm = null;
            Ask(SalvageCmd.Salvage, rows.Select(r => r.Token).ToArray());
        }
    }
}

/// <summary>The rule rows of salvageRules: on, kind, up to rarity, up to level, remove; "Add a rule". Returns the new
/// text when something changed.</summary>
internal static class SalvageRuleEditor
{
    private static readonly SalvageKind[] Kinds = Enum.GetValues<SalvageKind>();
    private static readonly GearRarity[] Rarities = Enum.GetValues<GearRarity>();
    private static readonly Dictionary<int, int> Dragging = new();   // render thread only

    public static string? Draw(UiDraw ui, float fs, bool german, string raw, string label, string help)
    {
        ui.Text(label);
        ui.Tooltip(help);
        var rules = SalvageRules.Parse(raw).ToList();
        var changed = false;
        if (rules.Count == 0) ui.TextDisabled(Texts.Get("salvageRulesEmpty", german));
        var kindNames = Kinds.Select(k => Texts.Get("salvageKind." + k, german)).ToArray();
        var rarityNames = Rarities.Select(r => Texts.Get("rarity." + r, german)).ToArray();
        for (var i = 0; i < rules.Count; i++)
            using (ui.Id("rule" + i))
            {
                var r = rules[i];
                var on = r.On;
                if (ui.Checkbox("###on", ref on)) { rules[i] = r = r with { On = on }; changed = true; }
                ui.Tooltip(Texts.Get("salvageRuleOn", german));
                ui.SameLine();
                var k = Array.IndexOf(Kinds, r.Kind);
                ui.NextItemWidth(fs * 8f);
                if (ui.Combo("###kind", ref k, kindNames) && k >= 0) { rules[i] = r = r with { Kind = Kinds[k] }; changed = true; }
                ui.SameLine();
                ui.Text(Texts.Get("salvageUpTo", german));
                ui.SameLine();
                var q = Array.IndexOf(Rarities, r.UpTo);
                ui.NextItemWidth(fs * 7f);
                if (ui.Combo("###rarity", ref q, rarityNames) && q >= 0) { rules[i] = r = r with { UpTo = Rarities[q] }; changed = true; }
                ui.SameLine();
                // the level is saved when the slider is let go, not at every step of the drag
                var level = Dragging.TryGetValue(i, out var d) ? d : r.MaxLevel;
                ui.NextItemWidth(fs * 5f);
                if (ui.Slider("###level", ref level, 0, SalvageRules.MaxLevelCap)) Dragging[i] = level;
                if (ui.LastItem.Deactivated && Dragging.Remove(i, out var done))
                {
                    rules[i] = r = r with { MaxLevel = done };
                    changed = true;
                }
                ui.Tooltip(Texts.Get("salvageMaxLevel", german));
                ui.SameLine();
                if (ui.SmallButton("x###x"))
                {
                    rules.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        if (rules.Count < SalvageRules.MaxRules && ui.SmallButton(Texts.Get("salvageAddRule", german) + "###addRule"))
        {
            rules.Add(new SalvageRule(SalvageKind.All, GearRarity.Common, 0, true));
            changed = true;
        }
        return changed ? SalvageRules.Format(rules) : null;
    }
}
