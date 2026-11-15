// SPDX-License-Identifier: MIT
// What the keys do: the menu key toggles the window, Esc closes it when open, the auto loot key flips
// looting (applied, saved and toasted by the caller). Kiln's menu has the keys while it is open (its Keys page takes
// presses as new bindings), so the mod's keys do nothing then, except closing the F8 window. Pure.
namespace KilnAutoLoot.Client;

internal enum MenuKeyAction { None, Open, Close }

internal static class KeyActions
{
    public static MenuKeyAction MenuKey(bool windowOpen, bool kilnMenuOpen) =>
        windowOpen ? MenuKeyAction.Close : kilnMenuOpen ? MenuKeyAction.None : MenuKeyAction.Open;

    public static bool EscapeCloses(bool windowOpen) => windowOpen;

    /// <summary>Whether the auto loot key flips looting now.</summary>
    public static bool ToggleActs(bool kilnMenuOpen) => !kilnMenuOpen;

    /// <summary>The new looting value and the toast's text key (Texts.Get).</summary>
    public static (bool On, string TextKey) Toggle(bool looting) => !looting ? (true, "toggleOn") : (false, "toggleOff");
}
