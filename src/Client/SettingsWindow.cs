// SPDX-License-Identifier: MIT
// The F8 window: the profile bar (ProfileBar), a status line (auto loot, gather focus), then one tab per page (Looting,
// Containers, Smashing, Filters, Where & bag, Ranges, World), then the footer, the restart note (only `enabled`) and
// the close button. Widgets: checkboxes, meter, seconds and count sliders, choice combos, the stack size slider and
// number field, the skip-list and only-list editors (the list that does not decide is folded away), the carry-limits
// editor; tooltips are the help texts. Personal settings are this
// game's; the World page is editable when this game hosts and read-only (from the server's answer) when joined. Under
// a personal setting the server applies differently, it says what the server uses.
// Render thread (Draw); Open/Close from the key handlers (main thread).
using System.Globalization;
using Kiln;
using KilnAutoLoot.Core;
using Texts = KilnAutoLoot.Core.Texts;
using KClient = Kiln.Client;

namespace KilnAutoLoot.Client;

/// <summary>The mod's keys as bound now, for the window's texts; Fixed = all four (a profile key may not be one).</summary>
internal sealed record WindowKeys(Func<string> Menu, Func<string> Toggle, Func<string> NextProfile, Func<string> Focus,
    Func<IEnumerable<string>> Fixed);

internal sealed class SettingsWindow(Data data, SettingsLink link, WindowKeys keys, FocusNotes focus,
    Action<Net.CommandKind> command, SalvagePanel salvage)
{
    private static readonly Color Orange = new(255, 166, 77);
    private static readonly Color OnGreen = new(140, 220, 150);
    private static readonly Color ValueText = new(242, 235, 224);
    private static readonly Color Clear = new(0, 0, 0, 0);

    private readonly object _lock = new();
    private readonly IgnoreEditor _ignore = new(data.Items);
    private readonly IgnoreEditor _only = new(data.Items, ListTexts.Only);
    private readonly CarryLimitEditor _carry = new(data.Items);
    private readonly ProfileBar _profiles = new(link, keys);
    private UiWindow? _window;
    private bool _windowGerman;
    private bool _german;
    private PersonalSettings _edit = new();
    private volatile bool _reload;
    private bool _dragging;                 // a personal slider or the stack field is being edited
    private readonly Dictionary<string, float> _worldDrag = new();
    private int? _stackDrag;
    private string _stackText = "";
    private bool _stackTextActive;

    public bool IsOpen => _window?.Open ?? false;

    /// <summary>The personal settings changed elsewhere (F9, a file edit): take them at the next frame.</summary>
    public void Reload() => _reload = true;

    public void Open()
    {
        var german = Texts.German(link.Personal.Language, GameLanguage.Current);
        lock (_lock)
        {
            if (_window is null || german != _windowGerman)
            {
                _window?.Dispose();
                _window = Ui.Window(string.Format(Texts.Get("title", german), AutoLootMod.Version), Draw,
                    new UiWindowOptions { StartOpen = false, AutoResize = true, Position = (0.5f, 0.5f) });
                _windowGerman = german;
            }
            _german = german;
            _edit = link.Personal.Clone();
            _reload = false;
            _dragging = false;
            _worldDrag.Clear();
            _stackDrag = null;
            _stackTextActive = false;
            _ignore.Reset();
            _only.Reset();
            _carry.Reset();
            _profiles.Reset();
        }
        _window.Show();
    }

    public void Close() => _window?.Hide();

    /// <summary>Opens the window (if closed) for the Salvage tab's confirm of the marked gear.</summary>
    public void OpenSalvageConfirm()
    {
        salvage.ConfirmAsked = true;
        if (!IsOpen) Open();
    }

    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
    }

    private string T(string key) => Texts.Get(key, _german);

    private void Draw(UiDraw ui)
    {
        lock (_lock)
        {
            if (KeyActions.EscapeCloses(IsOpen) && ui.KeyPressed(UiKey.Escape))
            {
                Close();
                return;
            }
            if (_reload && !_dragging)
            {
                _reload = false;
                _edit = link.Personal.Clone();
            }
            DrawWindow(ui);
        }
    }

    private void DrawWindow(UiDraw ui)
    {
        var cursor = ui.HasLayout ? ui.Cursor : default;
        var fs = cursor.FontSize > 0 ? cursor.FontSize : 16f * ui.Scale;
        var line = cursor.LineHeight > 0 ? cursor.LineHeight + fs * 0.45f : fs * 1.45f;
        var restartPending = false;
        var effective = link.Effective(_edit);

        ui.Space(fs * 30f, 0);   // one width for every page, so the window does not jump between tabs
        _profiles.Draw(ui, fs, _german);
        ui.Separator();
        DrawStatus(ui, effective);
        ui.Spacing();

        if (ui.BeginTabBar("pages"))
        {
            foreach (var page in SettingCatalog.Pages)
            {
                if (!ui.BeginTab(Texts.Section(page.Key, _german) + "###" + page.Key)) continue;
                using (ui.Id(page.Key))
                {
                    if (page.Key == "world") DrawWorldNotes(ui);
                    if (page.Key == "salvage") salvage.Draw(ui, fs, _german, (effective ?? _edit).SalvageConfirm);
                    foreach (var group in page.Groups)
                    {
                        ui.Spacing();
                        if (group.Heading is { } heading) ui.Separator(Texts.Section(heading, _german));
                        foreach (var spec in group.Settings)
                            using (ui.Id(spec.Key))
                                if (DrawEntry(ui, spec, fs, line, effective)) restartPending = true;
                    }
                }
                ui.EndTab();
            }
            ui.EndTabBar();
        }

        ui.Spacing();
        ui.Separator();
        ui.TextDisabled(T("footer"));
        if (link.SaveError is { } error) ui.TextWrapped(error);
        if (link.TooLarge) ui.TextWrapped(T("settingsTooLarge"));
        if (restartPending) ui.Text(Orange, T("restartNote"));
        ui.Spacing();
        if (ui.Button(string.Format(T("close"), keys.Menu()) + "###close")) Close();
    }

    /// <summary>Auto loot on or off and its key; how many settings the server caps; waiting for the server; data still
    /// loading.</summary>
    private void DrawStatus(UiDraw ui, PersonalSettings? effective)
    {
        var on = (effective ?? _edit).Looting;
        ui.Text(on ? OnGreen : Orange, T(on ? "statusOn" : "statusOff"));
        ui.SameLine();
        ui.TextDisabled(string.Format(T("statusKey"), keys.Toggle()));
        if (focus.Current is { } item and not 0)
        {
            var name = data.Items.TryGet(item, out var row) ? ItemTable.DisplayOf(row, _german) : "#" + item;
            ui.Text(OnGreen, Texts.Format("focusStatus", _german, name, keys.Focus()));
            ui.Tooltip(T("focusHelp"));
            ui.SameLine();
            if (ui.SmallButton(T("focusStop") + "###focusStop")) command(Net.CommandKind.ClearFocus);
        }
        else
        {
            ui.TextDisabled(Texts.Format("focusKey", _german, keys.Focus()));
            ui.Tooltip(T("focusHelp"));
        }
        if (effective is not null)
        {
            var capped = SettingValues.Differing(_edit, effective).Count();
            if (capped > 0) ui.Text(Orange, string.Format(T("statusCapped"), capped));
        }
        else if (KClient.IsRemote) ui.TextWrapped(T("statusWaiting"));
        if (data.Items.All.Count == 0) ui.TextDisabled(T("statusLoading"));
    }

    private void DrawWorldNotes(UiDraw ui)
    {
        ui.TextWrapped(link.World is null ? T("readOnlyServer") : link.WorldFromMenu ? T("menuNote") : T("hostNote"));
        ui.TextDisabled(T("defaultsNote"));
    }

    /// <summary>One setting. Returns true while a restart is pending for it (the `*`).</summary>
    private bool DrawEntry(UiDraw ui, SettingSpec spec, float fs, float line, PersonalSettings? effective)
    {
        var label = Texts.Label(spec.Key, _german);
        var help = Texts.Help(spec.Key, _german);
        if (spec.Owner == SettingOwner.Personal)
        {
            DrawPersonal(ui, spec, fs, line, label, help);
            if (effective is not null && !SettingValues.IsList(spec.Kind)
                && !Equals(SettingValues.Get(_edit, spec.Key), SettingValues.Get(effective, spec.Key)))
            {
                ui.TextDisabled(string.Format(T("serverApplies"), SettingValues.Describe(spec, effective, _german)));
                ui.Tooltip(help);
            }
            return false;
        }
        return DrawWorld(ui, spec, fs, label, help);
    }

    // ---------------------------------------------------------------- personal

    private void DrawPersonal(UiDraw ui, SettingSpec spec, float fs, float line, string label, string help)
    {
        switch (spec.Kind)
        {
            case WidgetKind.Bool:
                var b = SettingValues.GetBool(_edit, spec.Key);
                if (ui.Checkbox(label, ref b))
                {
                    SettingValues.Set(_edit, spec.Key, b);
                    link.Commit(_edit);
                }
                ui.Tooltip(help);
                break;
            case WidgetKind.Meters or WidgetKind.Seconds:
                var v = SettingValues.GetFloat(_edit, spec.Key);
                var (changed, released) = FormattedSlider(ui, spec, fs, ref v, label, help);
                if (changed)
                {
                    SettingValues.Set(_edit, spec.Key, SettingValues.Round(spec.Kind, v));
                    _dragging = true;
                }
                if (released)
                {
                    _dragging = false;
                    link.Commit(_edit);   // saved when released
                }
                break;
            case WidgetKind.Count:
                var n = (int)SettingValues.GetFloat(_edit, spec.Key);
                var (moved, done) = CountSlider(ui, spec, fs, ref n, label, help);
                if (moved)
                {
                    SettingValues.Set(_edit, spec.Key, n);
                    _dragging = true;
                }
                if (done)
                {
                    _dragging = false;
                    link.Commit(_edit);
                }
                break;
            case WidgetKind.Choice:
                var names = SettingValues.ChoiceNames(typeof(PersonalSettings), spec.Key);
                var shown = names.Select(v => Texts.Get(spec.Key + "." + v, _german)).ToArray();
                var index = SettingValues.ChoiceIndex(_edit, spec.Key);
                ui.NextItemWidth(fs * 16f);
                if (ui.Combo("##choice", ref index, shown) && index >= 0 && index < names.Count)
                {
                    SettingValues.Set(_edit, spec.Key, names[index]);
                    link.Commit(_edit);
                }
                ui.Tooltip(help);
                ui.SameLine();
                ui.Text(label);
                ui.Tooltip(help);
                break;
            case WidgetKind.ItemList:
                DrawItemList(ui, spec, fs, line, label, help);
                break;
            case WidgetKind.SalvageRules:
                var rules = SalvageRuleEditor.Draw(ui, fs, _german, _edit.SalvageRules ?? "", label, help);
                if (rules is not null)
                {
                    _edit.SalvageRules = rules;
                    link.Commit(_edit);
                }
                break;
            case WidgetKind.CarryLimits:
                var carry = _carry.Draw(ui, fs, line, _german, _edit.CarryLimits ?? "", label, help);
                if (carry is not null)
                {
                    _edit.CarryLimits = carry;
                    link.Commit(_edit);
                }
                break;
        }
        if (spec.Key == "visibleOnly" && link.Info is { LineOfSight: false })
            ui.TextWrapped(T("lineOfSightMissing"));
    }

    /// <summary>The skip list or the only list. The one that decides now (itemFilter) is open; the other is folded under
    /// a header that says it is not used. "Only this" on a suggestion switches to Only with that one item.</summary>
    private void DrawItemList(UiDraw ui, SettingSpec spec, float fs, float line, string label, string help)
    {
        var isOnly = spec.Key == "onlyList";
        var editor = isOnly ? _only : _ignore;
        var deciding = isOnly == (_edit.ItemFilter == FilterMode.Only);
        if (!deciding && !ui.CollapsingHeader(string.Format(T("listUnused"), label) + "###fold"))
            return;
        var raw = editor.Draw(ui, fs, line, _german, (isOnly ? _edit.OnlyList : _edit.SkipList) ?? "", label, help);
        if (raw is not null)
        {
            if (isOnly) _edit.OnlyList = raw;
            else _edit.SkipList = raw;
            link.Commit(_edit);   // every change is saved at once
        }
        if (editor.GatherOnly is { } row)
        {
            editor.GatherOnly = null;
            _edit.ItemFilter = FilterMode.Only;
            _edit.OnlyList = CarryLimitRows.ItemEntry(row);
            link.Commit(_edit);
        }
    }

    /// <summary>A whole-number slider with its value (or the zero text) drawn over it, then the label.</summary>
    private (bool Changed, bool Released) CountSlider(UiDraw ui, SettingSpec spec, float fs, ref int v, string label, string help)
    {
        ui.NextItemWidth(fs * 10f);
        bool changed;
        if (ui.HasLayout)
            using (ui.PushColor(UiColorSlot.Text, Clear)) changed = ui.Slider("##count", ref v, (int)spec.Min, (int)spec.Max);
        else changed = ui.Slider("##count", ref v, (int)spec.Min, (int)spec.Max);
        var it = ui.LastItem;
        v = Math.Clamp(v, (int)spec.Min, (int)spec.Max);
        if (ui.HasLayout)
            Centered(ui, it, v == 0 && spec.ZeroText is { } zero ? T(zero) : v.ToString(CultureInfo.InvariantCulture));
        ui.Tooltip(help);
        ui.SameLine();
        ui.Text(label);
        ui.Tooltip(help);
        return (changed, it.Deactivated);
    }

    /// <summary>A slider that shows "%.0f m" / "%.1f s" (Kiln's slider has no format: its own text is drawn clear and
    /// the value drawn over it), then the label. Returns (changed, released after a change).</summary>
    private (bool Changed, bool Released) FormattedSlider(UiDraw ui, SettingSpec spec, float fs, ref float v, string label, string help)
    {
        ui.NextItemWidth(fs * 10f);
        bool changed;
        if (ui.HasLayout)
            using (ui.PushColor(UiColorSlot.Text, Clear)) changed = ui.Slider("##slider", ref v, spec.Min, spec.Max);
        else changed = ui.Slider("##slider", ref v, spec.Min, spec.Max);
        var it = ui.LastItem;
        if (ui.HasLayout)
            Centered(ui, it, SettingValues.Round(spec.Kind, v) == 0 && spec.ZeroText is { } zero ? T(zero) : SettingValues.Format(spec.Kind, v));
        ui.Tooltip(help);
        ui.SameLine();
        ui.Text(label);
        ui.Tooltip(help);
        v = Math.Clamp(v, spec.Min, spec.Max);
        return (changed, it.Deactivated);
    }

    private static void Centered(UiDraw ui, UiItem it, string text)
    {
        var (w, h) = ui.MeasureText(text);
        ui.DrawText(UiLayer.Window, it.MinX + (it.Width - w) / 2f, it.MinY + (it.Height - h) / 2f, text, ValueText);
    }

    // ---------------------------------------------------------------- world settings and limits

    private bool DrawWorld(UiDraw ui, SettingSpec spec, float fs, string label, string help)
    {
        var world = link.World;
        if (world is null)
        {
            DrawReadOnly(ui, spec, label, help);
            return false;
        }
        object holder = spec.Owner == SettingOwner.Limit ? world.Value.Limits : world.Value;
        switch (spec.Kind)
        {
            case WidgetKind.Bool:
                var b = SettingValues.GetBool(holder, spec.Key);
                var pending = false;
                // the window shows the saved value; the `*` says the running world started with the other one
                if (spec.NeedsRestart) pending = SettingSpec.RestartPending(b, link.WorldEnabledInEffect);
                if (ui.Checkbox(label, ref b)) link.CommitWorld(spec.Path, b);
                ui.Tooltip(help);
                if (pending)
                {
                    ui.SameLine();
                    ui.Text(Orange, "*");
                }
                return pending;
            case WidgetKind.Meters:
                var v = _worldDrag.TryGetValue(spec.Key, out var d) ? d : SettingValues.GetFloat(holder, spec.Key);
                var (changed, released) = FormattedSlider(ui, spec, fs, ref v, label, help);
                if (changed) _worldDrag[spec.Key] = SettingValues.RoundMeters(v);
                if (released && _worldDrag.Remove(spec.Key, out var done)) link.CommitWorld(spec.Path, done);
                return false;
            case WidgetKind.Stack:
                DrawStack(ui, fs, world.Value.MaxStack, label, help);
                return false;
        }
        return false;
    }

    /// <summary>The log slider with "Default" at 0, the number field with -/+ (100; Ctrl 1000), then the
    /// label. Saved when the slider is released, the field is left, or a step button is pressed.</summary>
    private void DrawStack(UiDraw ui, float fs, int saved, string label, string help)
    {
        var value = _stackDrag ?? saved;
        var pos = StackField.ToPosition(value);
        ui.NextItemWidth(fs * 10f);
        bool moved;
        if (ui.HasLayout)
            using (ui.PushColor(UiColorSlot.Text, Clear)) moved = ui.Slider("##slider", ref pos, 0, StackField.Positions);
        else moved = ui.Slider("##slider", ref pos, 0, StackField.Positions);
        var it = ui.LastItem;
        if (moved) value = (_stackDrag = StackField.FromPosition(pos)).Value;
        if (ui.HasLayout) Centered(ui, it, StackField.Label(value, T("default")));
        ui.Tooltip(help);
        if (it.Deactivated && _stackDrag is { } dragged) CommitStack(dragged);

        ui.SameLine();
        ui.NextItemWidth(fs * 4.5f);
        if (!_stackTextActive) _stackText = StackField.Text(value);
        if (ui.InputText("##input", ref _stackText, 16) && StackField.Parse(_stackText) is { } typed) _stackDrag = typed;
        var field = ui.LastItem;
        _stackTextActive = field.Active;
        ui.Tooltip(help);
        if (field.Deactivated && _stackDrag is { } entered) CommitStack(entered);
        var ctrl = ui.HasLayout && (ui.HeldMods & KeyMods.Ctrl) != 0;
        ui.SameLine();
        if (ui.SmallButton("-")) CommitStack(StackField.Stepped(value, -1, ctrl));
        ui.SameLine();
        if (ui.SmallButton("+")) CommitStack(StackField.Stepped(value, +1, ctrl));
        ui.SameLine();
        ui.Text(label);
        ui.Tooltip(help);
    }

    private void CommitStack(int value)
    {
        _stackDrag = null;
        _stackTextActive = false;
        link.CommitWorld("maxStack", StackField.Clamp(value));
    }

    // ---------------------------------------------------------------- joined: what the server says

    private void DrawReadOnly(UiDraw ui, SettingSpec spec, string label, string help)
    {
        var info = link.Info;
        string value;
        if (info is null) value = "–";
        else if (spec.Key == "maxStack") value = StackField.Label(info.MaxStack, T("default"));
        else if (spec.Key == "backpackCheat") value = OnOff(info.BackpackCheat);
        else if (spec.Key == "enabled") value = OnOff(info.Enabled);   // in effect in the server's world
        else if (spec.Owner == SettingOwner.Limit && info.Limits is { } limits)
            value = SettingValues.Describe(spec, limits, _german);
        else value = "–";
        ui.TextDisabled($"{label}: {value}");
        ui.Tooltip(help);
    }

    private string OnOff(bool on) => _german ? (on ? "an" : "aus") : (on ? "on" : "off");
}
