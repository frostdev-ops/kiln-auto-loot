// SPDX-License-Identifier: MIT
// One player's loot state on the server (all state is per player). Created on join, dropped on leave and at the
// world's stop. Tick thread only.
using Kiln;
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Server;

internal sealed class PlayerLoot
{
    public PlayerLoot(EntityId entity, IClock clock)
    {
        Entity = entity;
        Visits = new VisitTable(clock);
        Full = new FullTracker(clock);
        Pickups = new PickupTracker(clock, Full);
        Notices = new NoticeBook(clock);
        Enemy = new EnemyPause(clock);
    }

    public EntityId Entity { get; }

    /// <summary>What the player's game sent (PersonalMsg, or SetLocalPersonal for the host), unclamped; null = the
    /// player has no Kiln Auto Loot (or has not sent yet): the server's defaults apply.</summary>
    public PersonalSettings? Sent { get; set; }

    /// <summary>The item ids the player's game matched <see cref="Sent"/>'s skip list to with its own item names (English
    /// and German, which a dedicated server lacks); null = none came (the hosting game's own player: the server part
    /// shares that game's item table).</summary>
    public uint[]? SentSkipIds { get; set; }

    /// <summary>The settings in effect: (Sent ?? ServerSettings.Defaults) clamped to ServerLimits.</summary>
    public PersonalSettings Settings { get; set; } = new();

    public IgnoreList? Ignore { get; set; }

    /// <summary>The latest settings message not applied yet (JSON and skip ids): applied on the tick, at most one per
    /// <see cref="LootLoop.SettingsGapMs"/>; a newer message replaces it.</summary>
    public (string Json, uint[] SkipIds)? PendingSettings { get; set; }

    /// <summary>When a settings message was last applied (null = never).</summary>
    public long? SettingsAppliedAt { get; set; }

    /// <summary>The player sent a skip list over the bounds once already (logged once).</summary>
    public bool OverBoundsLogged { get; set; }

    /// <summary>The player's game reports its window state (it has the mod, or it is the hosting game's own player).</summary>
    public bool ReportsWindow { get; set; }
    public bool WindowOpen { get; set; }

    public VisitTable Visits { get; }
    public FullTracker Full { get; }
    /// <summary>Shares <see cref="Full"/>'s 60 s gap: one "inventory full" notice per minute from pickups and containers.</summary>
    public PickupTracker Pickups { get; }
    public NoticeBook Notices { get; }
    public EnemyPause Enemy { get; }

    /// <summary>Line-of-sight answers per target key, valid until the time given (about 1 s).</summary>
    public Dictionary<ulong, (long Until, bool Visible)> Sight { get; } = new();

    /// <summary>Containers with Items.Move still in flight: key -> moves pending.</summary>
    public Dictionary<ulong, int> PendingMoves { get; } = new();

    /// <summary>Containers looted while the player had no free slot: key -> (takeable count then, time).</summary>
    public Dictionary<ulong, (int Takeable, long At)> Stalls { get; } = new();

    /// <summary>Next time Smash.Run is due (once per second).</summary>
    public long NextSmashMs { get; set; }
}
