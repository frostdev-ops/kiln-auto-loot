// SPDX-License-Identifier: MIT
// The few mod types Smash.cs and ProbeTrace.cs name but the lab probe does not compile (the mod's data record, the loot
// loop's clock, the server): enough for the lab to run the mod's own Smash.Run on the lab's world.
namespace KilnAutoLoot
{
    internal sealed class Data
    {
        public Server.SmashTable Smash { get; init; } = Server.SmashTable.Empty;
    }
}

namespace KilnAutoLoot.Server
{
    internal sealed class LootServer;

    internal sealed class LootLoop
    {
        internal static long StopwatchMicros() =>
            (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1_000_000.0 / System.Diagnostics.Stopwatch.Frequency));
    }
}
