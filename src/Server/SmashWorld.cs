// SPDX-License-Identifier: MIT
// The game calls of smashing, behind an interface so tests/Offline can drive Smash.Run with a fake.
// KilnSmashWorld is the real one: World.Find + Nearby.Within + Spawn.Inspect, the player's states
// (PlayerStates) and the hit (SmashHit). Every call is [ServerOnly] [TickThreadOnly].
using Kiln;

namespace KilnAutoLoot.Server;

/// <summary>A candidate's template and position; <paramref name="Serial"/> tells a reused entity id apart.</summary>
internal readonly record struct SmashTarget(EntityId Entity, Guid Template, ulong Serial, WorldPosition Position, long Stamp = 0);

internal interface ISmashWorld
{
    /// <summary>Every entity with a component all smash targets have (Destructible): one map pass, shared by all players.</summary>
    IReadOnlyList<EntityId> Candidates();
    IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius);
    SmashTarget? Inspect(EntityId entity);
    /// <summary>SpawnTime: distinguishes game-spawned entities (whose Kiln serial is zero) under reused ids.</summary>
    long SpawnStamp(EntityId entity);
    /// <summary>World.Position: where a target already inspected is now; null when it is gone.</summary>
    WorldPosition? PositionOf(EntityId entity);
    bool InSaveZone(EntityId player);
    bool NearBuildZone(EntityId player);
    /// <summary>One smash hit from <paramref name="player"/> on the target (fire and forget).</summary>
    void Hit(EntityId player, in SmashTarget target, SmashKind kind);
    /// <summary>The server can check line of sight (capability queries.line_of_sight).</summary>
    bool SightAvailable { get; }
    /// <summary>Queries.VoxelLineOfSight from the player to the target.</summary>
    Sight LineOfSight(EntityId player, EntityId target);
}

internal sealed class KilnSmashWorld : ISmashWorld
{
    /// <summary>The run-time component smash targets carry. The template's keen::ecs::Destructible would be the natural test, but
    /// that one (and MiningNode) is template data only: World.Find("Destructible") finds nothing on a running server
    /// (lab 2026-11-14). CheckPlayerInRange is on 1236 of the 1241 smash.txt templates and on a spawned barrel, tree and
    /// hive in the lab; RandomLootOnDestroy is a run-time component too (the barrel had it) and adds what has random loot.</summary>
    public const string TargetComponent = "CheckPlayerInRange";
    public const string ExtraComponent = "RandomLootOnDestroy";

    public IReadOnlyList<EntityId> Candidates()
    {
        var main = World.Find(TargetComponent);
        var extra = World.Find(ExtraComponent);
        if (extra.Count == 0) return main;
        var all = new HashSet<EntityId>(main);
        all.UnionWith(extra);
        return [.. all];
    }

    public IReadOnlyList<EntityId> Within(IReadOnlyList<EntityId> candidates, WorldPosition at, double radius) =>
        Nearby.Within(candidates, at, radius);

    public SmashTarget? Inspect(EntityId entity)
    {
        if (!Capabilities.Is("kiln.spawn")) return null;
        return Spawn.Inspect(entity) is { Template: { } template } info
            ? new SmashTarget(entity, template, info.Serial, info.Position, SpawnStamp(entity))
            : null;
    }

    public long SpawnStamp(EntityId entity) => World.TryRead<long>(entity, "SpawnTime", out var stamp) ? stamp : 0;

    public WorldPosition? PositionOf(EntityId entity) => World.Position(entity);

    // the two player states smashing tests: in a save zone (nothing at all) and near a build zone
    public bool InSaveZone(EntityId player) => PlayerStates.Is(player, PlayerStateId.InSaveZone);
    public bool NearBuildZone(EntityId player) => PlayerStates.Is(player, PlayerStateId.IsNearBuildingZone);

    public void Hit(EntityId player, in SmashTarget target, SmashKind kind) => SmashHit.Hit(player, target, kind);

    public bool SightAvailable => Capabilities.Is("queries.line_of_sight");

#pragma warning disable KILN121 // checked in SightAvailable, which Smash asks first
    public Sight LineOfSight(EntityId player, EntityId target)
    {
        var r = Queries.VoxelLineOfSight(player, target);
        if (r is { Ok: true }) return r.Visible ? Sight.Visible : Sight.Hidden;
        return r?.Reason == "BudgetExceeded" ? Sight.Later : Sight.Unknown;
    }
#pragma warning restore KILN121
}
