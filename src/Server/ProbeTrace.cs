// SPDX-License-Identifier: MIT
// Hooks for the probe build (dotnet build -p:KilnAutoLootProbe=true, src/Probe): what the loot loop and the smasher decide
// for the entities the probe watches. Every method here is [Conditional]: in any other build the calls are compiled out,
// arguments included, so the mod itself does not change.
using System.Diagnostics;
using Kiln;

namespace KilnAutoLoot.Server;

internal static class ProbeTrace
{
    public const string Symbol = "KILN_AUTOLOOT_PROBE";

#if KILN_AUTOLOOT_PROBE
    /// <summary>Set by the probe: whether an entity is watched, and where its lines go (tick thread).</summary>
    internal static Func<EntityId, bool>? Watching;
    internal static Action<EntityId, string>? Sink;
#endif

    /// <summary>One decision about a watched entity ("site: text"; the probe folds repeats of the same line).</summary>
    [Conditional(Symbol)]
    public static void Note(EntityId e, string text)
    {
#if KILN_AUTOLOOT_PROBE
        if (Watching?.Invoke(e) == true) Sink?.Invoke(e, text);
#endif
    }

    [Conditional(Symbol)]
    public static void WorldUp(LootServer server, LootLoop loop, Data data, ModContext? context)
    {
#if KILN_AUTOLOOT_PROBE
        Probe.ProbeConsole.WorldUp(server, loop, data, context);
#endif
    }

    [Conditional(Symbol)]
    public static void Tick()
    {
#if KILN_AUTOLOOT_PROBE
        Probe.ProbeConsole.Tick();
#endif
    }

    [Conditional(Symbol)]
    public static void WorldDown()
    {
#if KILN_AUTOLOOT_PROBE
        Probe.ProbeConsole.WorldDown();
#endif
    }
}
