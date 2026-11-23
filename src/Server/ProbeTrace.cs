// SPDX-License-Identifier: MIT
// What the loot loop and the smasher decide for an entity someone watches: the probe build's console
// (dotnet build -p:KilnAutoLootProbe=true, src/Probe) and, in every build since 0.2.8, "/autoloot near" (NearReport).
// Note costs nothing while nobody watches the entity: its interpolated text is only built for a watched one
// (NoteText). WorldUp, Tick and WorldDown are the probe build's only ([Conditional]: compiled out elsewhere).
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kiln;

namespace KilnAutoLoot.Server;

internal static class ProbeTrace
{
    public const string Symbol = "KILN_AUTOLOOT_PROBE";

    /// <summary>Whether an entity is watched, and where its lines go (tick thread; null = nobody watches).</summary>
    internal static Func<EntityId, bool>? Watching;
    internal static Action<EntityId, string>? Sink;

    /// <summary>One decision about a watched entity ("site: text").</summary>
    public static void Note(EntityId e, string text)
    {
        if (Watching?.Invoke(e) == true) Sink?.Invoke(e, text);
    }

    /// <summary>As above; the text is built only when the entity is watched.</summary>
    public static void Note(EntityId e, [InterpolatedStringHandlerArgument("e")] ref NoteText text)
    {
        if (text.On) Sink?.Invoke(e, text.Text());
    }

    /// <summary>An interpolated note's text, built only for a watched entity.</summary>
    [InterpolatedStringHandler]
    public ref struct NoteText
    {
        private DefaultInterpolatedStringHandler _inner;
        public readonly bool On;

        public NoteText(int literalLength, int formattedCount, EntityId e, out bool shouldAppend)
        {
            On = shouldAppend = Watching?.Invoke(e) == true;
            _inner = On ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        public void AppendLiteral(string value) => _inner.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);
        public string Text() => _inner.ToStringAndClear();
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
