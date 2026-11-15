// SPDX-License-Identifier: MIT
// The loop's clock: wall time summed from TickInfo.WallDelta (never step counts), so
// every millisecond rule runs by wall clock on a 24.5 Hz dedicated server and a 60 Hz host alike.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class TickClock : IClock
{
    // Starts well above 0: no rule may read "0 ms" as "never".
    private double _ms = 1_000_000;

    public long NowMs => (long)_ms;

    public void Advance(double wallDeltaMs)
    {
        if (double.IsFinite(wallDeltaMs) && wallDeltaMs > 0) _ms += Math.Min(wallDeltaMs, 10_000);
    }
}
