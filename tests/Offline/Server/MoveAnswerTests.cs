// SPDX-License-Identifier: MIT
// A planned partial take is not a receipt. Failed moves retry only after a delay and a new source read;
// pending moves are never duplicated, even when the result takes much longer than a normal server step.
using Kiln;
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class MoveAnswerTests
{
    private static (LoopRig Rig, FakeEntity Corpse) Corpse()
    {
        var rig = new LoopRig(s => { s.OpenDelay = 0; s.GroundLoot = false; s.SkipList = "=Material_T1_Twigs"; });
        var corpse = rig.Game.Add(LoopRig.Enemy, 2, [LootLoop.InventoryComponent]);
        corpse.Dead = true;  // keeps Enemy_Vukah_Melee's enemy template, as in the owner's test.2 log
        corpse.Slots = [new ContainerSlot(10, 1, 4), new ContainerSlot(11, 7, 5)];
        return (rig, corpse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unsuccessful_partial_take_retries_after_a_delay_with_a_fresh_source_and_no_queued_duplicate(bool noRoom)
    {
        var outcome = noRoom ? MoveOutcome.NoRoom : MoveOutcome.Failed;
        var (rig, corpse) = Corpse();
        rig.ScanAfter(100);
        var first = Assert.Single(rig.Game.Moves);
        Assert.Equal(11ul, first.Slot);
        Assert.False(rig.Loop.Counters.LootedItems.ContainsKey(7));  // dispatch is not a receipt
        rig.ScanFor(3000);
        Assert.Single(rig.Game.Moves);  // no answer: do not risk a second queued take
        Assert.Contains(rig.KeyLines, l => l.StartsWith("move:") && l.Contains("pending"));
        first.Done(outcome);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Moves);  // not a 10 Hz failure loop
        // Something else took the old stack. A retry must not replay its stale slot id.
        corpse.Slots = [new ContainerSlot(10, 1, 4), new ContainerSlot(12, 7, 2)];
        rig.ScanFor(1500);
        Assert.Equal(2, rig.Game.Moves.Count);
        Assert.Equal(12ul, rig.Game.Moves[1].Slot);
        Assert.False(rig.Loop.Counters.LootedItems.ContainsKey(7));
        Assert.Contains(rig.KeyLines, l => l.StartsWith("move:") && l.Contains(outcome.ToString()));
        Assert.Empty(rig.Game.Loots);
    }

    [Fact]
    public void An_enemy_template_that_dies_without_changing_identity_is_looted_slot_by_slot_not_as_a_live_enemy()
    {
        var (rig, corpse) = Corpse();
        corpse.Dead = false;
        rig.ScanAfter(100);
        Assert.Empty(rig.Game.Moves);
        corpse.Dead = true;
        rig.ScanAfter(100);
        Assert.Equal(11ul, Assert.Single(rig.Game.Moves).Slot);
        Assert.Contains(rig.KeyLines, l => l.Contains("Enemy_Test") && l.Contains("left Twigs x4") && l.Contains("Flint x5"));
        Assert.Empty(rig.Game.Loots);
    }
    [Fact]
    public void A_failed_transfer_is_not_requeued_on_the_very_next_100ms_scan()
    {
        var (rig, _) = Corpse();
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Moves).Done(MoveOutcome.Failed);
        rig.ScanAfter(100);
        Assert.Single(rig.Game.Moves);
    }

    [Theory]
    [InlineData(ItemsStatus.Done, 5)]
    [InlineData(ItemsStatus.Partial, 2)]
    [InlineData(ItemsStatus.NoRoom, 0)]
    [InlineData(ItemsStatus.Refused, 0)]
    [InlineData(ItemsStatus.Unavailable, 0)]
    [InlineData(ItemsStatus.SlotChanged, 0)]
    public void The_service_status_reason_and_committed_ledger_amount_survive_the_adapter(ItemsStatus status, int committed)
    {
        var result = new MoveResult(status, MoveKind.Stack, new EntityId(100), new EntityId(1), 5, committed, 1,
            committed == 0 ? [] : [new MoveChange(new SlotId(11), new SlotId(20), new ItemId(7), committed, EntityId.None)],
            "transaction reason", 27, status == ItemsStatus.Refused, 2, false);
        var answer = KilnGame.Answer(result, 11);
        Assert.Equal(status.ToString(), answer.Status);
        Assert.Contains("transaction reason", answer.Detail);
        Assert.Contains("game error 27", answer.Detail);
        Assert.Equal(committed, answer.Confirmed);
        Assert.Equal(committed, answer.Reported);
        Assert.Equal(0, KilnGame.Answer(result with { DryRun = true }, 11).Confirmed);
        Assert.Equal(0, KilnGame.Answer(result, 999).Confirmed);  // not another source slot's ledger
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Only_the_confirmed_amount_is_counted_and_a_noop_or_partial_answer_retries_safely(int confirmed)
    {
        var (rig, corpse) = Corpse();
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        var answer = new MoveAnswer(confirmed == 2 ? MoveOutcome.NoRoom : MoveOutcome.Done, confirmed,
            confirmed == 2 ? "Partial" : "Done", "service detail", confirmed);
        move.Done(answer);
        move.Done(answer);  // duplicate callbacks do not count twice or release another move
        Assert.Equal(confirmed, rig.Loop.Counters.LootedItems.GetValueOrDefault(7u));
        Assert.Contains(rig.KeyLines, l => l.StartsWith("move:") && l.Contains($"confirmed by Items.Move ledger {confirmed}"));
        if (confirmed < 5)
        {
            corpse.Slots = [new ContainerSlot(10, 1, 4), new ContainerSlot(12, 7, 5 - confirmed)];
            rig.ScanAfter(100);
            Assert.Single(rig.Game.Moves);
            rig.ScanAfter(LootLoop.MoveRetryMs);
            Assert.Equal(12ul, rig.Game.Moves[1].Slot);
            rig.ScanFor(3000);
            Assert.Equal(2, rig.Game.Moves.Count); // new attempt remains pending; don't queue it twice
        }
    }

    [Fact]
    public void Late_move_answers_from_a_previous_world_do_not_count_or_notify_in_the_new_one()
    {
        var (rig, _) = Corpse();
        rig.ScanAfter(100);
        var move = Assert.Single(rig.Game.Moves);
        rig.Loop.Reset();
        move.Done(new MoveAnswer(MoveOutcome.Done, 5, "Done"));
        Assert.Empty(rig.Loop.Counters.LootedItems);
        Assert.DoesNotContain(rig.KeyLines, l => l.StartsWith("move:"));
    }

    [Fact]
    public void Move_result_lines_are_capped_and_multiline_reasons_cannot_flood_the_log()
    {
        var (rig, corpse) = Corpse();
        for (var i = 0; i < LootLoop.MaxMoveLines + 5; i++)
        {
            corpse.Slots = [new ContainerSlot(10, 1, 4), new ContainerSlot((ulong)(20 + i), 7, 5)];
            rig.ScanAfter(100);
            rig.Game.Moves[^1].Done(new MoveAnswer(MoveOutcome.Done, 5, "Done", "a\nb\r" + new string('x', 1000), 5));
        }
        var lines = rig.KeyLines.Where(l => l.StartsWith("move:")).ToList();
        Assert.Equal(LootLoop.MaxMoveLines + 1, lines.Count);
        Assert.Contains("result/pending lines", lines[^1]);
        Assert.All(lines, l => { Assert.DoesNotContain('\n', l); Assert.DoesNotContain('\r', l); Assert.True(l.Length < 800); });
        Assert.Contains("planned, not receipt", rig.KeyLines[0]);
    }

}
