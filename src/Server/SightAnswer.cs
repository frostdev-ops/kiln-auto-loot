// SPDX-License-Identifier: MIT
// Actual voxel-ray answers, kept for the bounded /autoloot near trace, never a distance-based visibility override.
using Kiln;

namespace KilnAutoLoot.Server;

internal enum SightSample { Primary, CorpseBody, CorpseUpper }

internal readonly record struct SightHit(WorldPosition Position, double Distance, uint Material, ushort Voxel);
internal readonly record struct SightAnswer(Sight State, SightHit? Obstruction = null, string? Reason = null);

internal sealed record SightTrace(WorldPosition From, WorldPosition To, SightAnswer Primary, SightAnswer? Body = null, SightAnswer? Upper = null)
{
    public const double EyeHeight = 1.5;
    // Not collider bounds: two small heights in the origin column, never an endpoint moved sideways past a wall.
    public const double BodyHeight = 0.35;
    public const double UpperHeight = 0.65;

    public void Note(EntityId target, bool cached = false)
    {
        Sample(target, SightSample.Primary, Primary, cached);
        if (Body is { } body) Sample(target, SightSample.CorpseBody, body, cached);
        if (Upper is { } upper) Sample(target, SightSample.CorpseUpper, upper, cached);
    }

    public void Sample(EntityId target, SightSample sample, SightAnswer answer, bool cached = false) => ProbeTrace.Note(target,
        $"sight: {Name(sample)} from {Point(From, EyeHeight)} to {Point(To, Height(sample))}: {Describe(answer)}{(cached ? " (cached/retained)" : "")}");

    private static string Name(SightSample sample) => sample switch
    {
        SightSample.CorpseBody => "corpse body low",
        SightSample.CorpseUpper => "corpse body upper",
        _ => "primary",
    };

    private static double Height(SightSample sample) => sample switch
    {
        SightSample.CorpseBody => BodyHeight,
        SightSample.CorpseUpper => UpperHeight,
        _ => EyeHeight,
    };

    private static string Point(WorldPosition p, double height) => FormattableString.Invariant($"({p.X:R}, {p.Y + height:R}, {p.Z:R})");

    private static string Describe(SightAnswer answer) => answer.Obstruction is { } h
        ? FormattableString.Invariant($"blocked by voxel at ({h.Position.X:R}, {h.Position.Y:R}, {h.Position.Z:R}), distance {h.Distance:R} m, material {h.Material}, voxel 0x{h.Voxel:X4}")
        : answer.State == Sight.Visible ? "clear"
        : answer.State == Sight.Hidden ? "hidden without a voxel obstruction result; no body retry"
        : $"{answer.State}: {Reason(answer.Reason)} (not a hidden answer)";

    private static string Reason(string? reason)
    {
        var text = (reason ?? "no reason").Replace('\r', ' ').Replace('\n', ' ');
        return text[..Math.Min(text.Length, 120)];
    }
}

internal sealed record PendingCorpseSight(long Until, SightTrace Trace, SightSample Next, int Rays);
