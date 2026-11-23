// SPDX-License-Identifier: MIT
// Candidate discovery for one scan, shared by all players. World.Find itself is indivisible in Kiln dev.30.6; each
// call, then each position read, is a separate preparation turn in LootLoop's step budget. Unlike a far-position
// cache, a fresh list/position snapshot every scan cannot miss a new corpse or an entity teleported/reused nearby.
// Only positions are shared between component lists, never decisions or visibility (which remain per player).
using Kiln;

namespace KilnAutoLoot.Server;

internal sealed class CandidateIndex(IGame game, Func<long> micros)
{
    private readonly Dictionary<EntityId, WorldPosition?> _positions = new();
    private readonly Dictionary<string, List<(EntityId Id, WorldPosition At)>> _near = new(StringComparer.Ordinal);
    private readonly List<(WorldPosition At, double Reach)> _reach = [];

    /// <summary>This step's World.Find calls and position reads, with their individual and total times.</summary>
    public int Finds, Reads, Entities;
    public long FindMicros, ReadMicros, SlowestFind, SlowestRead;

    public void Begin(IEnumerable<(WorldPosition At, double Reach)> players)
    {
        _positions.Clear();
        _near.Clear();
        _reach.Clear();
        _reach.AddRange(players);
        Step();
    }

    public void Step()
    {
        Finds = Reads = Entities = 0;
        FindMicros = ReadMicros = SlowestFind = SlowestRead = 0;
    }

    public void Reset() => Begin([]);

    /// <summary>One bounded turn per Find/position read. The caller checks its deadline between turns, including
    /// cached positions and candidates outside every player's reach (not just the expensive game calls).</summary>
    public IEnumerable<bool> Prepare(string component)
    {
        if (_near.ContainsKey(component)) yield break;
        var near = _near[component] = [];
        var t0 = micros();
        var ids = game.Find(component);
        var spent = micros() - t0;
        FindMicros += spent;
        SlowestFind = Math.Max(SlowestFind, spent);
        Finds++;
        Entities += ids.Count;
        yield return true;
        foreach (var id in ids)
        {
            if (!_positions.TryGetValue(id, out var at))
            {
                t0 = micros();
                at = game.PositionOf(id);
                spent = micros() - t0;
                ReadMicros += spent;
                SlowestRead = Math.Max(SlowestRead, spent);
                Reads++;
                _positions[id] = at;
            }
            if (at is { } p && _reach.Any(r => p.DistanceTo(r.At) <= r.Reach)) near.Add((id, p));
            yield return true;
        }
    }

    public IReadOnlyList<(EntityId Id, WorldPosition At)> Near(string component) => _near[component];
}
