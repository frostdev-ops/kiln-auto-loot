// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Per player: "inventory full" from containers and the 60 s notice gap shared with pickups.</summary>
public sealed class FullTracker
{
    public const int Repeats = 100;
    public const long WindowMs = 5000;
    public const long NoticeGapMs = 60000;

    private readonly IClock _clock;
    private uint? _lastId;
    private int _repeats;
    private long _windowStart;
    private long? _lastNotice;

    public FullTracker(IClock clock) => _clock = clock;

    /// <summary>Count one item the slot filter let through (<see cref="SlotRule.Take"/>). True = show "inventory full"
    /// now: more than 100 attempts on this id within a 5000 ms window (it restarts on another id or after 5 s), and the
    /// shared 60 s gap allows a notice (taken by this call).</summary>
    public bool ContainerAttempt(uint itemId)
    {
        long now = _clock.NowMs;
        if (_lastId != itemId || now - _windowStart > WindowMs) { _lastId = itemId; _repeats = 0; _windowStart = now; }
        return ++_repeats > Repeats && MayNotify();
    }

    /// <summary>True (and the gap starts now) when no "inventory full" notice was shown in the last 60000 ms.</summary>
    public bool MayNotify()
    {
        long now = _clock.NowMs;
        if (_lastNotice is long t && now - t <= NoticeGapMs) return false;
        _lastNotice = now;
        return true;
    }
}
