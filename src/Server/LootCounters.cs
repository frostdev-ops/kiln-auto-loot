// SPDX-License-Identifier: MIT
// Kiln Auto Loot's counters, added up since the world started and written as two log lines every 5 s with counterLog.
// Tick thread only. One scan = every player once, each 100 ms.
using System.Text;

namespace KilnAutoLoot.Server;

internal sealed class LootCounters
{
    // the scan
    public long Scans;            // scans of all players
    public long Rechecks;         // 2-second marks at which every target is looked at again
    public long PlayerScans;      // one player scanned
    public long WindowPauses;     // ... skipped: a game window or the F8 window is open
    public long EnemyNear;        // ... an enemy was found within enemyDistance
    public long EnemyPauses;      // ... skipped: the enemy pause holds
    public long PlayersLooting;   // ... went on to look for targets

    // targets
    public long PickupsSeen, ContainersSeen;
    public long UnknownTemplates;     // a template the mod has no rule for
    public long ContainersHeldBack;   // a container the mod does not open in this state
    public long NotReleased;          // refused: the target's group is not one the mod takes automatically
    public long GroupOff;             // refused: the player switched the group off
    public long OutOfRange;           // refused: outside the group's radius
    public long YieldSkipped;         // refused: a plant or bed that would give a skipped item
    public long HeightOut;            // refused: above or below the player's height window
    public long RarityLow;            // left: gear below minGearRarity (on the ground or in a container slot)
    public long NotListed;            // left: not on the only list (itemFilter only)
    public long NotFocus;             // left: gather focus is on another item
    public long NoRoom;               // left: no room in carried stacks (fillCarriedStacks, keepFreeSlots) or the bag unreadable
    public long CarryLimited;         // left: the player carries the item's carry limit
    public long FocusOn;              // gather focus switched on

    // the container path
    public long ContainerTries;   // asked the visit table
    public long Waiting;          // still inside openDelay
    public long AlreadyHarvested; // harvested once in this visit
    public long Accepts;          // interaction sent (opens the container)
    public long LootAlls;         // loot-all sent to a container with a usable offer
    public long LootNoOffer;      // loot-all sent to a target without an offer
    public long LootOfferOff;     // loot-all sent while the offer was switched off
    public long QuestKept;        // quest item slots left in a container
    public long Unreadable;       // a container whose inventory could not be read: left, tried again next scan
    public long SkippedInContainers, SkippedPickups;   // skip-list items left behind

    // the pickup path
    public long PickupWaits;      // the pickup tracker held a pickup back
    public long FixedPickups;     // a pickup with its own inventory (mushrooms, stone piles)

    // Kiln side: injects sent, moves, the per-scan cap, hold-E stops, sight.
    public long Injects, Moves, CapReached, HoldECanceled, SightBlocked;

    /// <summary>Scan time of the last 5 s, in microseconds.</summary>
    public long ScanMicros, ScanCount, ScanMaxMicros;

    /// <summary>Item ids moved by the mod's loot (up to 64 ids, reset each line).</summary>
    public readonly Dictionary<uint, long> LootedItems = new();

    public void CountLooted(uint item, int count)
    {
        if (LootedItems.Count >= 64 && !LootedItems.ContainsKey(item)) return;
        LootedItems[item] = LootedItems.GetValueOrDefault(item) + count;
    }

    public string CountersLine() =>
        $"counters | scan: scans={Scans} rechecks={Rechecks} players={PlayerScans} windowPauses={WindowPauses} " +
        $"enemyNear={EnemyNear} enemyPauses={EnemyPauses} looking={PlayersLooting} " +
        $"| targets: pickups={PickupsSeen} containers={ContainersSeen} unknown={UnknownTemplates} heldBack={ContainersHeldBack} " +
        $"notReleased={NotReleased} groupOff={GroupOff} outOfRange={OutOfRange} yieldSkipped={YieldSkipped} " +
        $"height={HeightOut} rarity={RarityLow} notListed={NotListed} notFocus={NotFocus} noRoom={NoRoom} carryLimit={CarryLimited} " +
        $"focusOn={FocusOn} " +
        $"| containers: tries={ContainerTries} waiting={Waiting} harvested={AlreadyHarvested} accepts={Accepts} " +
        $"lootAll={LootAlls}/{LootNoOffer}/{LootOfferOff} questKept={QuestKept} unreadable={Unreadable} skipped={SkippedInContainers}/{SkippedPickups} " +
        $"| pickups: waits={PickupWaits} fixed={FixedPickups} " +
        $"| kiln: injects={Injects} moves={Moves} capReached={CapReached} holdEStopped={HoldECanceled} sightBlocked={SightBlocked}";

    /// <summary>The timing line with the items taken; resets their 5 s parts.</summary>
    public string TimingLine()
    {
        var avg = ScanCount == 0 ? 0 : ScanMicros / ScanCount;
        var sb = new StringBuilder($"timing (5 s): scans={ScanCount} total={ScanMicros} us avg={avg} us max={ScanMaxMicros} us");
        sb.Append(" | items taken (id x count):");
        foreach (var (id, n) in LootedItems) sb.Append($" {id}x{n}");
        ScanMicros = ScanCount = ScanMaxMicros = 0;
        LootedItems.Clear();
        return sb.ToString();
    }

    public string Summary() =>
        $"scans {PlayerScans}, accepts {Accepts}, loot-alls {LootAlls}, moves {Moves}, hold-E stopped {HoldECanceled}";
}
