// SPDX-License-Identifier: MIT
// The profile bar at the top of the F8 window: the profile combo (a choice switches at once), New (from the settings in
// use), Rename (name and the profile's own key), Delete (asks once more; never the last profile), Reset (built-ins
// only). New and Rename open one edit line under the bar with Save and Cancel. The book changes on the tick thread
// (SettingsLink); the bar reads its snapshot. Render thread.
using Kiln;
using Texts = KilnAutoLoot.Core.Texts;

namespace KilnAutoLoot.Client;

internal sealed class ProfileBar(SettingsLink link, WindowKeys keys)
{
    private static readonly Color Orange = new(255, 166, 77);

    private enum EditMode { None, New, Rename }

    private EditMode _mode;
    private string _editId = "";
    private string _name = "";
    private string _key = "";
    private string? _problem;
    private string? _confirmDelete;   // the profile whose Delete was pressed once

    public void Reset()
    {
        _mode = EditMode.None;
        _problem = null;
        _confirmDelete = null;
    }

    public void Draw(UiDraw ui, float fs, bool german)
    {
        string T(string key) => Texts.Get(key, german);
        if (link.Profiles is not { } book) return;
        var snap = book.Snapshot;
        var active = snap.Active;
        if (_confirmDelete is not null && _confirmDelete != active.Id) _confirmDelete = null;

        ui.Text(T("profile"));
        ui.SameLine();
        ui.NextItemWidth(fs * 12f);
        var names = snap.Profiles.Select(p => p.Display(german)).ToArray();
        var index = Math.Max(0, snap.Profiles.ToList().FindIndex(p => p.Id == active.Id));
        if (ui.Combo("##profile", ref index, names) && index >= 0 && index < snap.Profiles.Count
            && snap.Profiles[index].Id != active.Id)
        {
            _mode = EditMode.None;
            link.SwitchProfile(snap.Profiles[index].Id);
        }
        ui.Tooltip(string.Format(T("profileNextKey"), keys.NextProfile()));

        ui.SameLine();
        if (ui.SmallButton(T("profileNew") + "###new"))
            Open(EditMode.New, "", Texts.Format("profileNewName", german, book.NextNumber()), "");
        ui.Tooltip(T("profileNewHelp"));
        ui.SameLine();
        if (ui.SmallButton(T("profileRename") + "###rename"))
            Open(EditMode.Rename, active.Id, active.Display(german), active.Key ?? "");
        ui.Tooltip(T("profileRenameHelp"));
        if (snap.Profiles.Count > 1)
        {
            ui.SameLine();
            var sure = _confirmDelete == active.Id;
            if (sure)
            {
                using (ui.PushColor(UiColorSlot.Text, Orange))
                    if (ui.SmallButton(T("profileDeleteSure") + "###delete"))
                    {
                        _confirmDelete = null;
                        _mode = EditMode.None;
                        link.DeleteProfile(active.Id);
                    }
            }
            else if (ui.SmallButton(T("profileDelete") + "###delete")) _confirmDelete = active.Id;
            ui.Tooltip(T("profileDeleteHelp"));
        }
        if (active.BuiltIn)
        {
            ui.SameLine();
            if (ui.SmallButton(T("profileReset") + "###reset")) link.ResetProfile(active.Id);
            ui.Tooltip(T("profileResetHelp"));
        }
        if (link.ProfileError is { } error) ui.TextWrapped(Texts.Format("profileSaveError", german, error));
        if (link.SwitchError is { } failed) ui.TextWrapped(Texts.Format(failed.TextKey, german, failed.Detail ?? ""));

        if (_mode != EditMode.None) DrawEdit(ui, fs, german, book);
    }

    private void Open(EditMode mode, string id, string name, string key)
    {
        _mode = mode;
        _editId = id;
        _name = name;
        _key = key;
        _problem = null;
        _confirmDelete = null;
    }

    private void DrawEdit(UiDraw ui, float fs, bool german, ProfileBook book)
    {
        string T(string key) => Texts.Get(key, german);
        using (ui.Id("edit"))
        {
            ui.NextItemWidth(fs * 12f);
            ui.InputText(T("profileName") + "##name", ref _name, ProfileBook.MaxNameLength * 4);
            ui.SameLine();
            ui.NextItemWidth(fs * 6f);
            ui.InputText(T("profileKey") + "##key", ref _key, 64);
            ui.Tooltip(T("profileKeyHelp"));
            ui.SameLine();
            if (ui.SmallButton(T("profileSave") + "###save")) Save(german, book);
            ui.SameLine();
            if (ui.SmallButton(T("profileCancel") + "###cancel")) _mode = EditMode.None;
            if (_problem is not null) ui.Text(Orange, _problem);
        }
    }

    /// <summary>Checks the name and the key here, so the player sees why; the book checks them again.</summary>
    private void Save(bool german, ProfileBook book)
    {
        if (ProfileBook.Name(_name) is null) return;
        var key = ProfileKeys.Normalize(_key);
        var self = _mode == EditMode.Rename ? _editId : null;
        if (key is not null)
        {
            if (!KeyChord.TryParse(key, out _))
            {
                _problem = Texts.Get("profileKeyBad", german);
                return;
            }
            if (ProfileKeys.IsTaken(key, keys.Fixed()))
            {
                _problem = Texts.Format("profileKeyTaken", german, "Kiln Auto Loot");
                return;
            }
            if (book.KeyOwner(key, self) is { } owner)
            {
                _problem = Texts.Format("profileKeyTaken", german, owner.Display(german));
                return;
            }
        }
        if (_mode == EditMode.New) link.NewProfile(_name, key);
        else link.RenameProfile(_editId, _name, key);
        _mode = EditMode.None;
    }
}
