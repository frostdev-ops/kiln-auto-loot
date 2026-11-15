// SPDX-License-Identifier: MIT
// Gather focus as the player sees it. The focus key asks the server to gather only the item this player last took
// by hand (again = everything again); the server's answer (ServerInfoMsg.FocusItem, 0 = none) says what it does. A
// change of the focus gets a short note; a press the server answered with "none" while there was no focus means it has
// nothing to focus on yet: the hint says to take one item by hand first. The first answer after joining only tells
// what is in effect (no note unless the player pressed the key). Thread-safe (the answers come in on any thread). Pure.
namespace KilnAutoLoot.Client;

internal enum FocusNoteKind { None, On, Off, Hint }

internal readonly record struct FocusNote(FocusNoteKind Kind, uint Item);

internal sealed class FocusNotes
{
    private readonly object _lock = new();
    private uint? _known;
    private bool _asked;

    /// <summary>The focus the server last said (0 = none), or null before its first answer.</summary>
    public uint? Current
    {
        get { lock (_lock) return _known; }
    }

    /// <summary>The focus key was pressed and the command went out.</summary>
    public void Asked()
    {
        lock (_lock) _asked = true;
    }

    /// <summary>Left the server or the world: the next answer starts afresh.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _known = null;
            _asked = false;
        }
    }

    /// <summary>A server answer: the note to show, if any.</summary>
    public FocusNote OnAnswer(uint focus)
    {
        lock (_lock)
        {
            var was = _known;
            var asked = _asked;
            _known = focus;
            if (was == focus || (was is null && focus == 0))
            {
                // no change: only an answer to a press says something (a press with nothing to focus on)
                if (!asked) return default;
                _asked = false;
                return focus == 0 ? new(FocusNoteKind.Hint, 0) : default;
            }
            _asked = false;
            if (was is null && !asked) return default;   // what was in effect when we joined
            return focus == 0 ? new(FocusNoteKind.Off, was ?? 0) : new(FocusNoteKind.On, focus);
        }
    }
}
