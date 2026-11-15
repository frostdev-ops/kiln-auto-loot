// SPDX-License-Identifier: MIT
// The notices: two lines with the mod's name in front, shown for 3 s. Kiln draws them with Hud.Toast (its own place
// and look: bottom right, stacked, the newest at the bottom). A toast with the same text as one still on screen is
// not added again. Pure: the caller shows it.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Client;

internal static class NoticeText
{
    public const string ModName = "Kiln Auto Loot";
    public const long ToastMs = 3000;

    /// <summary>The toast text for a server notice, or null when there is nothing to show (an item without a name in
    /// items.txt).</summary>
    public static string? For(NoticeKind kind, string? itemName, string menuKey, bool german, Func<string, bool, string> get) => kind switch
    {
        NoticeKind.InventoryFull => Compose(get("fullLine1", german), get("fullLine2", german)),
        NoticeKind.LeftBehind when !string.IsNullOrEmpty(itemName) =>
            Compose(string.Format(get("leftBehindLine1", german), itemName), string.Format(get("leftBehindLine2", german), menuKey)),
        _ => null,
    };

    public static string Compose(string line1, string? line2 = null) =>
        string.IsNullOrEmpty(line2) ? $"{ModName}  {line1}" : $"{ModName}  {line1}\n{line2}";
}

/// <summary>Keeps the same text off the screen twice at once.</summary>
internal sealed class ToastBook(IClock clock)
{
    private readonly Dictionary<string, long> _shown = new();

    public bool ShouldShow(string text)
    {
        lock (_shown)
        {
            var now = clock.NowMs;
            if (_shown.TryGetValue(text, out var at) && now - at < NoticeText.ToastMs) return false;
            if (_shown.Count > 64)
                foreach (var old in _shown.Where(p => now - p.Value >= NoticeText.ToastMs).Select(p => p.Key).ToList()) _shown.Remove(old);
            _shown[text] = now;
            return true;
        }
    }
}

internal sealed class WallClock : IClock
{
    public long NowMs => Environment.TickCount64;
}
