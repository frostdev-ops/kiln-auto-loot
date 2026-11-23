// SPDX-License-Identifier: MIT
// 0.2.8, the owner's hosted game: a Vukah corpse holding Animal Fur and Resin, right next to him, never looked into and
// no kiln.log line about it. Spawn.Inspect's serial is Kiln's own record of what Kiln spawned, 0 for what the game spawns
// (corpses, drops), and the game hands a freed entity id to the next new entity. A corpse that took the id of something
// the loop saw in the scan before kept that one's template: a ground pickup (refused with groundLoot off) or a living
// enemy (never looted), each scan, without a line. The entity's SpawnTime tells the two apart.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class EntityIdReuseTests
{
    private const uint Apple = 6, Flint = 7;

    private static FakeEntity CorpseIn(LoopRig rig, Kiln.EntityId id)
    {
        var corpse = rig.Game.Reuse(id, LoopRig.Vukah, 3, LoopRig.OfferInv);
        corpse.Serial = 0;                 // spawned by the game: Kiln keeps no record of it
        corpse.Offer = Offer.None;
        corpse.Slots = [new ContainerSlot(10, Flint, 6), new ContainerSlot(11, Apple, 5)];
        return corpse;
    }

    [Fact]
    public void A_corpse_that_took_the_id_of_a_pickup_seen_a_scan_before_is_looted_as_a_corpse()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.GroundLoot = false; });
        var twig = rig.Game.Add(LoopRig.Twig, 3, [], pickupItem: 1);
        twig.Serial = 0;
        rig.ScanAfter(100);                // looked at: a ground pickup, left (groundLoot off)
        Assert.Empty(rig.Game.Loots);
        var corpse = CorpseIn(rig, twig.Id);
        rig.ScanFor(1000);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
    }

    [Fact]
    public void A_corpse_that_took_the_id_of_a_living_enemy_seen_a_scan_before_is_looted_as_a_corpse()
    {
        var rig = new LoopRig(s => s.OpenDelay = 0);
        var enemy = rig.Game.Add(LoopRig.Enemy, 3, [LootLoop.InventoryComponent]);   // alive: never looted
        enemy.Serial = 0;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Loots);
        var corpse = CorpseIn(rig, enemy.Id);
        rig.ScanFor(1000);
        Assert.Contains(rig.Game.Loots, l => l.Host == corpse.Id);
    }
}
