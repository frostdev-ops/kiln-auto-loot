// SPDX-License-Identifier: MIT
namespace KilnAutoLoot.Core;

/// <summary>Per player: the pickup rules: one take per 1500 ms, stuck 3000 ms then 10000 ms back off.</summary>
public sealed class PickupTracker
{
    public const long TakeGapMs = 1500;
    public const long GapMs = 1000;         // unseen longer = a new episode
    public const long StuckMs = 3000;
    public const long BackoffMs = 10000;
    public const long RecentMs = 250;
    public const int Capacity = 256;

    private sealed class Pickup
    {
        public ulong Key;
        public long FirstSeen, LastSeen, BlockedUntil;
        public long? LastTake;
    }

    private readonly IClock _clock;
    private readonly FullTracker? _full;
    private readonly Dictionary<ulong, Pickup> _byKey = new();
    private long? _lastNotice;

    public PickupTracker(IClock clock) : this(clock, null) { }

    /// <summary>With <paramref name="full"/>, the "inventory full" notice shares that tracker's 60 s gap with containers;
    /// without, this source keeps its own 60 s gap (each source has its own).</summary>
    public PickupTracker(IClock clock, FullTracker? full)
    {
        _clock = clock;
        _full = full;
    }

    /// <summary>Call for every pickup in range; false = do not take it now. <paramref name="raiseFull"/> = show "inventory full".
    /// <paramref name="wanted"/> = Decide allowed it and its item is not ignored. While not wanted the episode start
    /// moves to now (no full hint for ignored items). Wanted and still offered more than 3000 ms after the episode
    /// start: blocked for 10000 ms, then a fresh try. A pickup taken less than 1500 ms ago is not taken again (it lies
    /// there a moment after it was taken).</summary>
    public bool MayTake(ulong pickup, bool wanted, out bool raiseFull)
    {
        raiseFull = false;
        long now = _clock.NowMs;
        if (!_byKey.TryGetValue(pickup, out var s))
        {
            if (_byKey.Count >= Capacity)
            {
                Pickup oldest = null!;
                foreach (var v in _byKey.Values) if (oldest is null || v.LastSeen < oldest.LastSeen) oldest = v;
                _byKey.Remove(oldest.Key);
            }
            s = new Pickup { Key = pickup, FirstSeen = now, LastSeen = now };
            _byKey[pickup] = s;
        }
        else if (now - s.LastSeen > GapMs)
        {
            s.FirstSeen = now; s.BlockedUntil = 0;   // a new episode
        }
        s.LastSeen = now;
        if (!wanted) { s.FirstSeen = now; return false; }
        bool stuck = s.BlockedUntil > now;
        if (!stuck && now - s.FirstSeen > StuckMs)
        {
            s.BlockedUntil = now + BackoffMs;
            s.FirstSeen = s.BlockedUntil;   // after the pause the pickup gets a fresh try
            stuck = true;
            raiseFull = _full?.MayNotify() ?? OwnGap(now);
        }
        if (stuck) return false;
        if (s.LastTake is long t && now - t < TakeGapMs) return false;
        return true;
    }

    /// <summary>Call after the take for this pickup was sent.</summary>
    public void Taken(ulong pickup)
    {
        if (_byKey.TryGetValue(pickup, out var s)) s.LastTake = _clock.NowMs;
    }

    /// <summary><see cref="MayTake"/> ran for it within 250 ms. A container target that is such a
    /// pickup (the game declined it) is never accepted or looted.</summary>
    public bool SeenRecently(ulong pickup) =>
        _byKey.TryGetValue(pickup, out var s) && _clock.NowMs - s.LastSeen <= RecentMs;

    /// <summary>The pickup is in its 10 s back off now.</summary>
    public bool Blocked(ulong pickup) => _byKey.TryGetValue(pickup, out var s) && s.BlockedUntil > _clock.NowMs;

    private bool OwnGap(long now)
    {
        if (_lastNotice is long t && now - t <= FullTracker.NoticeGapMs) return false;
        _lastNotice = now;
        return true;
    }
}
