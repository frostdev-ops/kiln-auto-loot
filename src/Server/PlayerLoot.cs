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

    /// <summary>The player's game has handed its settings over (a message that did not read counts too: the player has
    /// the mod). Until then, or until <see cref="LootLoop.SettingsWaitMs"/> in game, the mod does nothing for them.</summary>
    public bool OwnSettings { get; set; }

    /// <summary>When a scan first saw the player in game (alive, with a position); null = not yet.</summary>
    public long? FirstSeenMs { get; set; }

    /// <summary>The wait for the player's own settings ran out and the key line saying so went out.</summary>
    public bool DefaultsSaid { get; set; }

    /// <summary>What the player's game sent (PersonalMsg, or SetLocalPersonal for the host), unclamped; null = the
    /// player has no Kiln Auto Loot (or has not sent yet): the server's defaults apply.</summary>
    public PersonalSettings? Sent { get; set; }

    /// <summary>The item ids the player's game matched <see cref="Sent"/>'s skip list to with its own item names (English
    /// and German, which a dedicated server lacks); null = none came (the hosting game's own player: the server part
    /// shares that game's item table).</summary>
    public uint[]? SentSkipIds { get; set; }

    /// <summary>The settings in effect: (Sent ?? ServerSettings.Defaults) clamped to ServerLimits.</summary>
    public PersonalSettings Settings { get; set; } = new();

    /// <summary>The item ids the player's game matched <see cref="Sent"/>'s only list to (as <see cref="SentSkipIds"/>).</summary>
    public uint[]? SentOnlyIds { get; set; }

    public IgnoreList? Ignore { get; set; }

    /// <summary>The only list as an item set (used with itemFilter only).</summary>
    public IgnoreList? Only { get; set; }

    public CarryLimits Limits { get; set; } = CarryLimits.None;

    /// <summary>Skip list, only list and gather focus as one decision; rebuilt with the settings and the focus.</summary>
    public ItemFilter Filter { get; set; } = ItemFilter.SkipOnly(new IgnoreList("", ItemTable.Empty()));

    /// <summary>The item the player last took by hand (a single pickup), 0 = none yet.</summary>
    public uint HandItem { get; set; }

    /// <summary>A single pickup the player asked for by hand, not yet seen arriving: the item and until when.</summary>
    public (uint Item, long Until)? HandPending { get; set; }

    /// <summary>The command budget: the start of the current second and the commands in it.</summary>
    public long? CommandWindowStart { get; set; }
    public int CommandCount { get; set; }

    /// <summary>A command that changed nothing was answered already (no second answer until something changes).</summary>
    public bool QuietAnswered { get; set; }

    /// <summary><see cref="Settings"/> as JSON, written once per change.</summary>
    public string? EffectiveJson { get; set; }

    /// <summary>The gather focus item, 0 = off. Dropped when the player leaves and at the world's stop.</summary>
    public uint Focus { get; set; }

    /// <summary>The bag as this scan sees it (read at the scan's first need), and the scan it belongs to.</summary>
    public Bag? Bag { get; set; }
    public long BagScan { get; set; } = -1;
    public bool BagUnreadable { get; set; }

    /// <summary>The latest settings message not applied yet (JSON, skip ids, only ids): applied on the tick, at most one
    /// per <see cref="LootLoop.SettingsGapMs"/>; a newer message replaces it.</summary>
    public (string Json, uint[] SkipIds, uint[] OnlyIds)? PendingSettings { get; set; }

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

    /// <summary>When a container's pending move batch began, for the capped "still pending" diagnostic.</summary>
    public Dictionary<ulong, long> PendingSince { get; } = new();

    /// <summary>A failed/partial/no-op batch waits until this time, then reads the source again before retrying.</summary>
    public Dictionary<ulong, long> MoveRetryAt { get; } = new();

    /// <summary>Items.Move amounts sent and not answered yet, per item (added to the next bag reads).</summary>
    public Dictionary<uint, int> MovesInFlight { get; } = new();

    /// <summary>Containers looted while the player had no free slot: key -> (takeable count then, time).</summary>
    public Dictionary<ulong, (int Takeable, long At)> Stalls { get; } = new();

    /// <summary>Farm animals (group livestock): key -> the next time their inventory is read
    /// (<see cref="LootLoop.LivestockEveryMs"/> after the last read).</summary>
    public Dictionary<ulong, long> LivestockNext { get; } = new();

    /// <summary>Next time Smash.Run is due (once per second).</summary>
    public long NextSmashMs { get; set; }
}
