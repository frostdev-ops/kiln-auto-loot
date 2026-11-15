// SPDX-License-Identifier: MIT
// "A game window is open" for the server: auto loot pauses unless the game shows plain
// play (Ui.Screens: Kind InGame and Hud InGame), and while the F8 window or Kiln's own menu is open. The game's UI state
// is read, not the mouse cursor, so a controller is covered too. The value is sent only when it
// changes (and again after a new connection or world). Pure: the caller passes what Kiln.Ui reports.
using Kiln;

namespace KilnAutoLoot.Client;

internal static class WindowSignal
{
    /// <param name="screensKnown">Ui.Screens.Available: without the game's UI hooks the screen is unknown, and only the
    /// F8 window and Kiln's menu pause (a paused-for-ever mod would be worse).</param>
    public static bool WindowOpen(bool screensKnown, ScreenKind kind, HudScreen hud, bool kilnMenuOpen, bool f8Open)
    {
        if (f8Open || kilnMenuOpen) return true;                   // the F8 menu counts as a window
        if (!screensKnown) return false;
        return !(kind == ScreenKind.InGame && hud == HudScreen.InGame);
    }
}

/// <summary>Sends the window state only when it changed since the last send that got through.</summary>
internal sealed class WindowSignalSender
{
    private bool? _sent;

    /// <summary>Forget what was sent (a new world or a new server connection): the next <see cref="Update"/> sends.</summary>
    public void Reset() => _sent = null;

    /// <summary>Returns true when <paramref name="send"/> was called. A send that fails (false) is tried again on the
    /// next update.</summary>
    public bool Update(bool open, Func<bool, bool> send)
    {
        if (_sent == open) return false;
        if (send(open)) _sent = open;
        return true;
    }

    public bool? LastSent => _sent;
}
