// SPDX-License-Identifier: MIT
// 0.2.0's rules never saw the gear Auto Loot itself takes out of corpses and chests: those come over as Kiln moves of
// the slot, which raise no pickup event. The loop now reports the item entities each of its moves brought.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class SalvageMovesTests
{
    [Fact]
    public void A_slot_move_from_a_container_reports_the_item_entities_it_brought()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CarryLimits = "=Flint:3"; });   // a readable pile under a bag rule: slot moves
        var taken = new List<(EntityId Player, EntityId From, IReadOnlyList<EntityId> Items)>();
        rig.Loop.Moved = (p, from, items) => taken.Add((p, from, items));
        var bones = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.InventoryComponent]);
        bones.Slots = [new ContainerSlot(11, 7, 5)];
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        move.Moved!([new EntityId(777)]);
        move.Done(MoveOutcome.Done);
        var t = Assert.Single(taken);
        Assert.Equal((rig.Player, bones.Id), (t.Player, t.From));
        Assert.Equal([new EntityId(777)], t.Items);
    }

    [Fact]
    public void A_move_that_brought_no_item_entity_reports_nothing()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CarryLimits = "=Flint:3"; });   // a readable pile under a bag rule: slot moves
        var calls = 0;
        rig.Loop.Moved = (_, _, _) => calls++;
        var bones = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.InventoryComponent]);
        bones.Slots = [new ContainerSlot(11, 7, 5)];
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        move.Moved!([]);
        move.Done(MoveOutcome.Done);
        Assert.Equal(0, calls);
    }
}
