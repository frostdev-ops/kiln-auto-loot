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

/// <summary>One press that reaches a key twice counts once. Kiln drops a held key's repeats by its own held state, which
/// a focus change clears, and a bouncing switch sends down, up, down: seen on a player's PC as the F8 window opening and
/// closing again 40 ms later. A press of the same key within <see cref="WindowMs"/> of the last press it took is that
/// press. Key handlers (the game's window thread); locked anyway. Pure.</summary>
internal sealed class KeyDebounce(Core.IClock clock)
{
    public const long WindowMs = 250;

    private readonly object _lock = new();
    private readonly Dictionary<string, long> _taken = new(StringComparer.Ordinal);

    /// <summary>Whether this press of <paramref name="key"/> acts. When it does not, <paramref name="sinceMs"/> is the
    /// time since the press it repeats.</summary>
    public bool Take(string key, out long sinceMs)
    {
        var now = clock.NowMs;
        lock (_lock)
        {
            if (_taken.TryGetValue(key, out var last) && now - last < WindowMs)
            {
                sinceMs = now - last;
                return false;
            }
            _taken[key] = now;
        }
        sinceMs = 0;
        return true;
    }
}
