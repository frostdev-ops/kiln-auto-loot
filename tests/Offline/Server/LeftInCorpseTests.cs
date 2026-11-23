// SPDX-License-Identifier: MIT
// What stays in a corpse, chest or creature pile, and why (HadToRegister, 2026-11-20: with Twigs on the skip list, Vukah
// corpses holding Animal Fur and Twigs seemed never looted). A readable corpse gives what the filter takes by slot moves
// and keeps the skipped stacks; the game removes a corpse only once it is empty, so it stays. kiln.log says so once per
// corpse, and once per creature pile left whole because its contents cannot be seen. Also: the window pause, as when the
// player opens such a corpse by hand.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class LeftInCorpseTests
{
    // the fixture's items: 1 Twigs, 4 Old Key, 6 Apple, 7 Flint (Flint stands in for the Vukah's Animal Fur)
    private const uint Twigs = 1, OldKey = 4, Apple = 6, Flint = 7;

    private static (LoopRig Rig, FakeEntity Corpse) VukahCorpse(Action<PersonalSettings>? more = null, uint[]? quest = null)
    {
        var rig = new LoopRig(s =>
        {
            s.OpenDelay = 0;
            s.SkipList = "=Material_T1_Twigs";
            more?.Invoke(s);
        }, quest: quest);
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(10, Flint, 6), new ContainerSlot(11, Twigs, 2)];
        return (rig, corpse);
    }

    private static List<string> LootLines(LoopRig rig) => rig.KeyLines.Where(l => l.StartsWith("loot: ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void A_corpse_holding_a_skipped_stack_gives_the_rest_by_slot_moves_and_keeps_the_skipped_one()
    {
        var (rig, corpse) = VukahCorpse();
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);              // the accept
        corpse.Offer = new Offer(true, false, 6);       // the game reacted
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);       // the flint, by itself
        Assert.Equal(10ul, move.Slot);
        Assert.Empty(rig.Game.Loots);                   // never a loot-all: it would take the twigs too

        corpse.Slots = [new ContainerSlot(11, Twigs, 2)];
        move.Done(MoveOutcome.Done);
        rig.Game.Clear();
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Moves);                   // the twigs stay, and so does the corpse
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.SkippedInContainers > 0);
    }

    [Fact]
    public void A_corpse_that_keeps_a_skipped_stack_says_so_once_in_kiln_log()
    {
        var (rig, corpse) = VukahCorpse();
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        corpse.Slots = [new ContainerSlot(11, Twigs, 2)];
        move.Done(MoveOutcome.Done);
        rig.ScanFor(5000);

        var line = Assert.Single(LootLines(rig));
        Assert.Contains($"player {rig.Player}", line);
        Assert.Contains("LootContainer_Enemy_Corpse_Vukah_Test", line);
        Assert.Contains("left Twigs x2 (on the skip list)", line);
        Assert.Contains("taking Flint x6", line);
        Assert.Contains("the game removes a corpse only once it is empty", line);
    }

    [Fact]
    public void A_corpse_holding_only_skipped_stacks_says_nothing_else_is_taken()
    {
        var (rig, corpse) = VukahCorpse();
        corpse.Slots = [new ContainerSlot(11, Twigs, 2)];
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(2000);
        Assert.Empty(rig.Game.Moves);
        Assert.Empty(rig.Game.Loots);
        var line = Assert.Single(LootLines(rig));
        Assert.Contains("left Twigs x2 (on the skip list)", line);
        Assert.Contains("nothing else to take", line);
    }

    [Fact]
    public void A_chest_line_names_each_reason_and_says_nothing_about_corpses()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Material_T1_Twigs"; }, quest: [OldKey]);
        var chest = rig.Game.Add(LoopRig.Chest, 4, [LootLoop.InventoryComponent]);   // no offer: taken at once
        chest.Slots = [new ContainerSlot(10, OldKey, 1), new ContainerSlot(11, Twigs, 3), new ContainerSlot(12, Apple, 2)];
        rig.ScanAfter(100);
        Assert.Equal([12ul], rig.Game.Moves.Select(m => m.Slot).ToArray());
        var line = Assert.Single(LootLines(rig));
        Assert.Contains("Old Key x1 (a quest item)", line);
        Assert.Contains("Twigs x3 (on the skip list)", line);
        Assert.Contains("taking Apple x2", line);
        Assert.DoesNotContain("corpse", line);
    }

    [Fact]
    public void A_corpse_taken_whole_writes_no_line()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Material_T1_Twigs"; });
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(10, Flint, 6)];   // no twigs in this one
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Loots);                      // its template can give twigs; this corpse has none: loot-all
        Assert.Empty(LootLines(rig));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unread_creature_pile_left_whole_for_a_skipped_yield_says_so_once(bool unreadable)
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Apple"; });
        var bones = rig.Game.Add(LoopRig.Bones, 3, LoopRig.OfferInv);
        bones.Offer = new Offer(true, false, 1);
        bones.Slots = unreadable ? null : [];
        rig.ScanAfter(100);
        bones.Offer = new Offer(true, false, 2);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);                       // as before: its contents cannot be seen
        var line = Assert.Single(LootLines(rig));
        Assert.Contains("LootPickup_Material_Skeleton_Hound_Bones_Test", line);
        Assert.Contains("left whole", line);
        Assert.Contains(unreadable ? "cannot be read" : "reads empty", line);
        Assert.Contains("Apple (on the skip list)", line);
    }

    [Fact]
    public void An_unread_creature_pile_with_no_known_yield_left_whole_in_only_mode_says_why()
    {
        // a pile whose yield holds no listed item is refused before (LootRules.Decide); one with no known yield gets here
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.ItemFilter = FilterMode.Only; s.OnlyList = "=Material_T1_Twigs"; });
        var parts = rig.Game.Add(LoopRig.Parts, 3, [LootLoop.OfferComponent]);
        parts.Offer = new Offer(true, false, 1);
        parts.Slots = null;
        rig.ScanAfter(100);
        parts.Offer = new Offer(true, false, 2);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        var line = Assert.Single(LootLines(rig));
        Assert.Contains("left whole", line);
        Assert.Contains("what it gives is not known, and the only list takes a pile only for an item it can give", line);
    }

    [Fact]
    public void An_unread_creature_pile_left_whole_for_a_bag_rule_says_so()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CarryLimits = "=Flint:3"; });
        var bones = rig.Game.Add(LoopRig.Bones, 3, [LootLoop.OfferComponent]);
        bones.Offer = new Offer(true, false, 1);
        bones.Slots = null;
        rig.ScanAfter(100);
        bones.Offer = new Offer(true, false, 2);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Loots);
        var line = Assert.Single(LootLines(rig));
        Assert.Contains("left whole: its inventory cannot be read", line);
        Assert.Contains("a bag rule is on, and it needs the amounts", line);
    }

    // ---------------------------------------------------------------- the player opens the corpse by hand

    [Theory]
    [InlineData(2_000)]    // a short look: the visit goes on
    [InlineData(15_000)]   // longer than VisitTable.ReArmMs: a new visit (accept again) once the window closes
    public void A_corpse_the_player_has_open_waits_and_gives_the_rest_once_the_window_closes(long openMs)
    {
        var (rig, corpse) = VukahCorpse();
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Interacts);
        rig.Loop.SetWindowOpen(rig.Player, true);           // the player pressed E on it: the game's loot window
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(openMs);
        Assert.Empty(rig.Game.Moves);                       // nothing while a window is open
        Assert.Empty(rig.Game.Loots);
        Assert.True(rig.Loop.Counters.WindowPauses > 0);

        rig.Loop.SetWindowOpen(rig.Player, false);
        rig.ScanFor(10_000);
        var move = Assert.Single(rig.Game.Moves);           // retried after the close: the flint, the twigs stay
        Assert.Equal(10ul, move.Slot);
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void A_corpse_the_player_emptied_by_hand_but_for_the_skipped_stack_is_left_after_the_window_closes()
    {
        var (rig, corpse) = VukahCorpse();
        rig.ScanAfter(100);
        rig.Loop.SetWindowOpen(rig.Player, true);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(2000);
        corpse.Slots = [new ContainerSlot(11, Twigs, 2)];   // the player took the flint
        rig.Loop.SetWindowOpen(rig.Player, false);
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Moves);
        Assert.Empty(rig.Game.Loots);
    }

    // ---------------------------------------------------------------- corpses seen but not looked into

    private static List<string> UnreadLines(LoopRig rig) => LootLines(rig).Where(l => l.Contains("not looked into")).ToList();

    [Fact]
    public void A_corpse_outside_the_height_window_says_so_once()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.VisibleOnly = false; s.HeightBelow = 4; });
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv, y: -6);   // down a slope
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(10, Flint, 6)];
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Interacts);
        var line = Assert.Single(UnreadLines(rig));
        Assert.Contains("LootContainer_Enemy_Corpse_Vukah_Test", line);
        Assert.Contains("6.0 m below you, deeper than Depth below me (4 m)", line);
    }

    [Fact]
    public void A_corpse_beyond_the_corpse_radius_says_so()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.CorpsesRadius = 5; });
        var corpse = rig.Game.Add(LoopRig.Vukah, 8, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        rig.ScanFor(1000);
        Assert.Contains("beyond the corpse radius (5 m)", Assert.Single(UnreadLines(rig)));
    }

    [Fact]
    public void An_elite_corpse_with_Bosses_off_says_so()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.Bosses = false; });
        var corpse = rig.Game.Add(LoopRig.EliteCorpse, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        rig.ScanFor(1000);
        Assert.Contains("a boss, mini boss or elite corpse, and Bosses is off", Assert.Single(UnreadLines(rig)));
    }

    [Fact]
    public void A_corpse_out_of_sight_says_so()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.VisibleOnly = true; });
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Sight = Sight.Hidden;
        rig.ScanFor(3000);
        Assert.Empty(rig.Game.Interacts);
        Assert.Contains("not in plain view from where you stand", Assert.Single(UnreadLines(rig)));
    }

    [Fact]
    public void A_bush_left_for_the_skip_list_writes_no_corpse_line()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.SkipList = "=Material_PlantFibre"; });
        rig.Game.Add(LoopRig.Bush, 3, LoopRig.OfferInv);
        rig.ScanFor(1000);
        Assert.Empty(LootLines(rig));
    }

    [Fact]
    public void A_corpse_first_out_of_reach_then_looted_gets_both_lines()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.VisibleOnly = false; s.HeightBelow = 4; s.SkipList = "=Material_T1_Twigs"; });
        var corpse = rig.Game.Add(LoopRig.Vukah, 3, LoopRig.OfferInv, y: -6);
        corpse.Offer = new Offer(true, false, 5);
        corpse.Slots = [new ContainerSlot(10, Flint, 6), new ContainerSlot(11, Twigs, 2)];
        rig.ScanFor(1000);
        corpse.Position = new Kiln.WorldPosition(3, -2, 0);   // the player walked down to it
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(500);
        Assert.Single(rig.Game.Moves);
        Assert.Equal(2, LootLines(rig).Count);
        Assert.Single(UnreadLines(rig));
    }

    // 0.2.8 (HadToRegister, Tyrion67): what a corpse can give is read from the game data (its default inventory); loot the
    // game adds at the death can be missing from that list. A readable corpse is never refused up front because every
    // item of its list is skipped (or none is listed): it is read, and each stack is judged.
    [Fact]
    public void A_readable_corpse_whose_known_yield_is_all_skipped_is_still_read_and_gives_the_rest()
    {
        var (rig, corpse) = VukahCorpse(s => s.SkipList = "=Material_T1_Twigs, =Flint");   // its yield: flint, twigs
        corpse.Slots = [new ContainerSlot(10, Apple, 3), new ContainerSlot(11, Twigs, 2)];  // apple: not in that list
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(500);
        var move = Assert.Single(rig.Game.Moves);
        Assert.Equal(10ul, move.Slot);
        Assert.Empty(rig.Game.Loots);
        Assert.Contains("left Twigs x2 (on the skip list)", Assert.Single(LootLines(rig)));
    }

    [Fact]
    public void A_readable_corpse_whose_known_yield_has_no_listed_item_is_still_read_in_only_mode()
    {
        var (rig, corpse) = VukahCorpse(s => { s.ItemFilter = FilterMode.Only; s.OnlyList = "=Food_Apple"; });
        corpse.Slots = [new ContainerSlot(10, Apple, 3), new ContainerSlot(11, Twigs, 2)];
        rig.ScanAfter(100);
        corpse.Offer = new Offer(true, false, 6);
        rig.ScanFor(500);
        Assert.Equal(10ul, Assert.Single(rig.Game.Moves).Slot);
        Assert.Empty(rig.Game.Loots);
    }
}
