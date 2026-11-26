// SPDX-License-Identifier: MIT
// "Keep bag slots free" (keepFreeSlots) for everything the mod takes (Celphia26, 0.2.13): with it above 0, plants and
// corpses were never collected while ground items were. Plants, crops and most creature piles have no inventory of their
// own (DefaultInventory only, lab dev31-autoloot-slots: Items.Read null), so their amounts cannot be read before the
// loot-all hands them over; the free-slot rule refused every one of them for that. It only needs to know how many new
// stacks a take can start: one per item it gives that the player does not already carry a stack of with room.
// Review (0.2.13): "with room" means room for the most it can give (its default inventory's countMax times the world's
// Loot Amount); a carried stack with less room makes the rest start a new stack in a kept slot, so it counts as one.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class FreeSlotsTests
{
    private const uint Twigs = 1, Fibre = 3, Apple = 6, Flint = 7;
    private static readonly string[] Inv = [LootLoop.InventoryComponent];

    private static LoopRig Rig(int keep, int free, Action<PersonalSettings>? more = null, params BagSlot[] carried)
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.KeepFreeSlots = keep; more?.Invoke(s); }, items: Stacked());
        rig.Game.BagSlots = carried.ToList();
        rig.Game.FreeSlotCount = free;
        return rig;
    }

    /// <summary>The fixture's items, each with a max stack as in the game (plant fibre 50, apples 20, flint 30).</summary>
    private static ItemTable Stacked() =>
        ItemTable.FromRows(Fixtures.Items().All.Select(r => r.Id switch
        {
            Fibre => r with { OriginalMaxStack = 50 },
            Apple => r with { OriginalMaxStack = 20 },
            Flint => r with { OriginalMaxStack = 30 },
            _ => r,
        }));

    private static bool Took(LoopRig rig, FakeEntity e) =>
        rig.Game.Loots.Any(l => l.Host == e.Id) || rig.Game.Moves.Any(m => m.From == e.Id);

    // ---------------------------------------------------------------- plants and crops (no inventory of their own)

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void A_plant_without_an_inventory_is_harvested_while_slots_above_the_kept_ones_are_free(int keep)
    {
        var rig = Rig(keep, keep + 5);
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;                          // Items.Read null: a DefaultInventory and no Inventory
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));
        Assert.Equal(0, rig.Loop.Counters.HarvestUnread);
    }

    [Fact]
    public void A_ripe_crop_without_an_inventory_is_harvested_with_free_slots_kept()
    {
        var rig = Rig(2, 6);
        var crop = rig.Game.Add(LoopRig.Crop, 2, Inv);
        crop.Slots = null;
        rig.ScanFor(500);
        Assert.True(Took(rig, crop));
    }

    [Fact]
    public void A_plant_is_left_once_only_the_kept_slots_are_free()
    {
        var rig = Rig(2, 2);
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bush));
        Assert.True(rig.Loop.Counters.NoRoom > 0);
    }

    [Fact]
    public void A_plant_tops_up_a_carried_stack_also_when_only_the_kept_slots_are_free()
    {
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 10, true));
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));               // plant fibre merges into the stack the player carries
    }

    [Fact]
    public void Two_plants_in_one_scan_share_the_one_usable_slot()
    {
        var rig = Rig(2, 3);
        var a = rig.Game.Add(LoopRig.Bush, 2, Inv);
        var b = rig.Game.Add(LoopRig.Crop, 3, Inv);   // also plant fibre: the first harvest's new stack has room for it
        var c = rig.Game.Add(LoopRig.Bones, 4, [LootLoop.OfferComponent]);   // apples: a new stack, no slot left for it
        a.Slots = b.Slots = c.Slots = null;
        rig.ScanAfter(100);
        Assert.True(Took(rig, a));
        Assert.True(Took(rig, b));
        Assert.False(Took(rig, c));
    }

    [Fact]
    public void A_plant_does_not_top_up_a_carried_stack_with_less_room_than_it_can_give()
    {
        // plant fibre 49 of 50: room for 1, the bush gives up to 3, so the rest would start a stack in a kept slot
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 49, true));
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bush));
        Assert.True(rig.Loop.Counters.NoRoom > 0);

        var exact = Rig(2, 2, null, new BagSlot(Fibre, 47, true));   // room for 3: all it can give
        var bush2 = exact.Game.Add(LoopRig.Bush, 2, Inv);
        bush2.Slots = null;
        exact.ScanFor(500);
        Assert.True(Took(exact, bush2));
    }

    [Fact]
    public void A_plant_tops_up_a_stack_on_the_action_bar_while_only_the_kept_slots_are_free()
    {
        // lab: the game's loot-all of a default inventory tops up an action-bar stack
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 10, false, ActionBar: true));
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));
    }

    [Fact]
    public void A_plant_with_less_room_in_its_carried_stack_than_it_gives_needs_a_usable_slot()
    {
        var rig = Rig(2, 3, null, new BagSlot(Fibre, 49, true));   // one usable slot takes the rest
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.True(Took(rig, bush));
    }

    [Theory]
    [InlineData(1.0, true)]    // up to 3: fits the 5 of room
    [InlineData(2.0, false)]   // up to 6: does not
    [InlineData(1.5, true)]    // up to 4.5, so 5: fits
    [InlineData(0.25, true)]   // a Loot Amount below 1 counts as 1
    public void The_worlds_loot_amount_scales_what_a_plant_can_give(double lootAmount, bool takes)
    {
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 45, true));
        rig.Game.LootAmount = lootAmount;
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.Equal(takes, Took(rig, bush));
    }

    [Fact]
    public void A_plant_whose_most_is_not_known_needs_a_usable_slot_for_each_item_even_when_carried()
    {
        // algae: plant fibre, how much is not known: a carried stack may take only part of it
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 10, true));
        var algae = rig.Game.Add(LoopRig.Algae, 2, Inv);
        algae.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, algae));

        var room = Rig(2, 3, null, new BagSlot(Fibre, 10, true));
        var algae2 = room.Game.Add(LoopRig.Algae, 2, Inv);
        algae2.Slots = null;
        room.ScanFor(500);
        Assert.True(Took(room, algae2));
    }

    [Fact]
    public void Harvests_in_one_scan_reserve_the_most_each_can_give()
    {
        // room for 5 plant fibre, no usable slot: the first bush (up to 3) fits, the second and the crop no longer do
        var rig = Rig(2, 2, null, new BagSlot(Fibre, 45, true));
        var a = rig.Game.Add(LoopRig.Bush, 2, Inv);
        var b = rig.Game.Add(LoopRig.Bush, 3, Inv);
        var c = rig.Game.Add(LoopRig.Crop, 4, Inv);
        a.Slots = b.Slots = c.Slots = null;
        rig.ScanAfter(100);
        Assert.True(Took(rig, a));
        Assert.False(Took(rig, b));
        Assert.False(Took(rig, c));
    }

    [Fact]
    public void Piles_whose_yield_is_not_known_share_the_one_usable_slot()
    {
        var rig = Rig(2, 3);
        var a = rig.Game.Add(LoopRig.Parts, 2, LoopRig.OfferInv);
        var b = rig.Game.Add(LoopRig.Parts, 3, LoopRig.OfferInv);
        a.Slots = [];
        b.Slots = [];
        rig.ScanAfter(100);
        Assert.Equal(1, new[] { a, b }.Count(x => Took(rig, x)));
    }

    [Fact]
    public void A_readable_plant_is_still_judged_by_its_exact_contents()
    {
        var rig = Rig(2, 3);
        var tree = rig.Game.Add(LoopRig.Bush, 2, Inv);   // a hazelnut tree has an inventory with its nuts in it
        tree.Slots = [new ContainerSlot(1, Fibre, 8), new ContainerSlot(2, Twigs, 4)];
        rig.ScanFor(500);
        Assert.False(Took(rig, tree));              // two new stacks, one usable slot

        var fits = Rig(2, 4);
        var tree2 = fits.Game.Add(LoopRig.Bush, 2, Inv);
        tree2.Slots = [new ContainerSlot(1, Fibre, 8), new ContainerSlot(2, Twigs, 4)];
        fits.ScanFor(500);
        Assert.True(Took(fits, tree2));
    }

    // ---------------------------------------------------------------- creature piles

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_creature_pile_is_taken_with_free_slots_kept(bool unreadable)
    {
        var rig = Rig(2, 6);
        var bones = rig.Game.Add(LoopRig.Bones, 3, unreadable ? [LootLoop.OfferComponent] : LoopRig.OfferInv);
        bones.Slots = unreadable ? null : [];      // bones and bat fur: no inventory; critter parts: one that reads empty
        rig.ScanFor(500);
        Assert.True(Took(rig, bones));
    }

    [Fact]
    public void A_creature_pile_needs_a_usable_slot_for_each_item_it_gives_that_is_not_carried()
    {
        var rig = Rig(2, 3);                          // one usable slot; the pile gives apples and flint
        var bones = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.OfferComponent]);
        bones.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bones));

        var carried = Rig(2, 3, null, new BagSlot(Apple, 1, true));
        var bones2 = carried.Game.Add(LoopRig.Bones, 3, [LootLoop.OfferComponent]);
        bones2.Slots = null;
        carried.ScanFor(500);
        Assert.True(Took(carried, bones2));          // the apples merge, the flint takes the usable slot
    }

    [Fact]
    public void A_creature_pile_without_a_known_yield_counts_as_one_new_stack()
    {
        var rig = Rig(2, 3);
        var parts = rig.Game.Add(LoopRig.Parts, 3, LoopRig.OfferInv);
        parts.Slots = [];
        rig.ScanFor(500);
        Assert.True(Took(rig, parts));

        var full = Rig(2, 2);
        var parts2 = full.Game.Add(LoopRig.Parts, 3, LoopRig.OfferInv);
        parts2.Slots = [];
        full.ScanFor(500);
        Assert.False(Took(full, parts2));
    }

    [Fact]
    public void A_creature_pile_left_for_the_kept_slots_says_so_in_kiln_log()
    {
        var rig = Rig(2, 2);
        var bones = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.OfferComponent]);
        bones.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bones));
        var line = Assert.Single(rig.KeyLines, l => l.Contains("left whole"));
        Assert.Contains("bag slots free above the ones kept free", line);
    }

    // ---------------------------------------------------------------- corpses and chests (readable, slot by slot)

    [Fact]
    public void A_corpse_is_emptied_whole_while_its_stacks_fit_above_the_kept_slots()
    {
        var rig = Rig(3, 8);
        var corpse = rig.Game.Add(LoopRig.Corpse, 2, Inv);
        corpse.Slots = [new ContainerSlot(1, Twigs, 4), new ContainerSlot(2, Flint, 3)];
        rig.ScanAfter(100);                          // one scan: the fake corpse keeps its slots after the loot-all
        Assert.Single(rig.Game.Loots);
        Assert.Empty(rig.Game.Moves);
    }

    [Fact]
    public void A_corpse_gives_what_fits_and_keeps_the_rest_once_the_usable_slots_run_out()
    {
        var rig = Rig(3, 4, null, new BagSlot(Twigs, 10, true));
        var corpse = rig.Game.Add(LoopRig.Corpse, 2, Inv);
        corpse.Slots = [new ContainerSlot(1, Flint, 4), new ContainerSlot(2, Apple, 1), new ContainerSlot(3, Twigs, 3)];
        rig.ScanFor(500);
        Assert.Empty(rig.Game.Loots);
        // flint takes the one usable slot, the apple would start another, the twigs top up the carried stack
        Assert.Equal([1ul, 3ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
    }

    // ---------------------------------------------------------------- the other bag rules still need the amounts

    [Fact]
    public void Fill_carried_stacks_still_leaves_a_harvest_whose_amounts_cannot_be_read()
    {
        var rig = Rig(2, 10, s => s.FillCarriedStacks = true, new BagSlot(Fibre, 10, true));
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bush));
        Assert.True(rig.Loop.Counters.HarvestUnread > 0);
    }

    [Fact]
    public void A_carry_limit_on_what_a_plant_gives_still_needs_its_amounts()
    {
        var rig = Rig(2, 10, s => s.CarryLimits = "=Plant Fiber:100");
        var bush = rig.Game.Add(LoopRig.Bush, 2, Inv);
        bush.Slots = null;
        rig.ScanFor(500);
        Assert.False(Took(rig, bush));
        Assert.True(rig.Loop.Counters.HarvestUnread > 0);

        var other = Rig(2, 10, s => s.CarryLimits = "=Apple:1");   // names nothing the bush gives
        var bush2 = other.Game.Add(LoopRig.Bush, 2, Inv);
        bush2.Slots = null;
        other.ScanFor(500);
        Assert.True(Took(other, bush2));
    }
}
