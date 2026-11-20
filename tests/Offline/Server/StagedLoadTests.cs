// SPDX-License-Identifier: MIT
// The game data load in steps (Server/GameData/StagedLoad), from a player's perf capture (inbox 50): on a hard disk the
// read, the classifiers and the cache write held the main menu's first frame for over 5 s. The caller's thread now only
// starts the read, names the items after a fresh read and takes the result; the read, the classifiers and the cache
// write run on workers.
using KilnAutoLoot.Core;
using KilnAutoLoot.Server.GameData;
using Xunit;

namespace KilnAutoLoot.Offline.LootServerTests;

public sealed class StagedLoadTests
{
    private static readonly GameFacts Facts = new([], [new ItemFacts(1, "Twig", Guid.NewGuid(), "Materials", 20, "", "",
        [], new Dictionary<string, string>())]);

    /// <summary>A load whose steps record their thread; the read can be held back with <see cref="Gate"/>.</summary>
    private sealed class Rig
    {
        public readonly ManualResetEventSlim Gate = new(true);
        public bool Available = true;
        public bool Named;
        public GameFacts? ReadFacts = Facts;
        public Exception? ReadThrows;
        public int Reads, Names, Classifies, Saves;
        public int ReadThread, NameThread, ClassifyThread, SaveThread;
        public string SavedKey = "";
        public readonly StagedLoad<string> Load;

        public Rig() => Load = new StagedLoad<string>(
            () => Available,
            () =>
            {
                Reads++;
                ReadThread = Environment.CurrentManagedThreadId;
                Gate.Wait();
                if (ReadThrows is not null) throw ReadThrows;
                return new ReadResult(ReadFacts, Named, Named ? "cache" : "game", "key-1");
            },
            f =>
            {
                Names++;
                NameThread = Environment.CurrentManagedThreadId;
                return f with { Items = f.Items.Select(i => i with { English = "Twig" }).ToList() };
            },
            f =>
            {
                Classifies++;
                ClassifyThread = Environment.CurrentManagedThreadId;
                return string.Join(",", f.Items.Select(i => i.English.Length > 0 ? i.English : i.Name));
            },
            (key, _) =>
            {
                Saves++;
                SaveThread = Environment.CurrentManagedThreadId;
                SavedKey = key;
            });

        /// <summary>Polls as a tick would, waiting between polls, until the load is no longer pending.</summary>
        public LoadState PollUntilSettled()
        {
            for (var i = 0; i < 500; i++)
            {
                var s = Load.Step();
                if (s != LoadState.Pending) return s;
                try { Load.Running?.Wait(TimeSpan.FromSeconds(5)); }
                catch (AggregateException) { /* the next Step reports it */ }
            }
            throw new TimeoutException("the load never settled");
        }
    }

    [Fact]
    public void The_first_step_only_starts_the_read_and_never_waits_for_it()
    {
        var rig = new Rig();
        rig.Gate.Reset();   // the read is stuck on the disk
        Assert.Equal(LoadState.Pending, rig.Load.Step());
        for (var i = 0; i < 100; i++) Assert.Equal(LoadState.Pending, rig.Load.Step());   // frames go on
        Assert.Null(rig.Load.Result);
        rig.Gate.Set();
        Assert.Equal(LoadState.Ready, rig.PollUntilSettled());
        Assert.Equal(1, rig.Reads);
    }

    [Fact]
    public void A_fresh_read_is_named_on_the_polling_thread_classified_and_saved_on_workers()
    {
        var rig = new Rig();
        var me = Environment.CurrentManagedThreadId;
        Assert.Equal(LoadState.Ready, rig.PollUntilSettled());
        Assert.Equal("Twig", rig.Load.Result);   // classified after naming
        // a blocking wait: an await frees this thread, and the pool could then run the save on it
        Assert.True(rig.Load.Saving!.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal((1, 1, 1, 1), (rig.Reads, rig.Names, rig.Classifies, rig.Saves));
        Assert.Equal(me, rig.NameThread);
        Assert.NotEqual(me, rig.ReadThread);
        Assert.NotEqual(me, rig.ClassifyThread);
        Assert.NotEqual(me, rig.SaveThread);
        Assert.Equal("key-1", rig.SavedKey);
        Assert.Equal("game", rig.Load.Source);
    }

    [Fact]
    public void A_cache_hit_is_neither_named_again_nor_written_again()
    {
        var rig = new Rig { Named = true };
        Assert.Equal(LoadState.Ready, rig.PollUntilSettled());
        Assert.Equal((1, 0, 1, 0), (rig.Reads, rig.Names, rig.Classifies, rig.Saves));
        Assert.Null(rig.Load.Saving);
        Assert.Equal(0, rig.Load.NameMs);
    }

    [Fact]
    public void Without_the_game_data_it_waits_and_reads_again_later()
    {
        var rig = new Rig { Available = false };
        Assert.Equal(LoadState.NotYet, rig.Load.Step());
        Assert.Equal(0, rig.Reads);
        rig.Available = true;
        rig.ReadFacts = null;   // the game has not loaded its data yet
        Assert.Equal(LoadState.NotYet, rig.PollUntilSettled());
        rig.ReadFacts = Facts;
        Assert.Equal(LoadState.Ready, rig.PollUntilSettled());
        Assert.Equal(2, rig.Reads);
    }

    [Fact]
    public void A_failed_read_is_reported_and_the_next_step_tries_again()
    {
        var rig = new Rig { ReadThrows = new InvalidDataException("bad blob") };
        Assert.Equal(LoadState.Failed, rig.PollUntilSettled());
        Assert.Equal("bad blob", rig.Load.Error?.Message);
        rig.ReadThrows = null;
        Assert.Equal(LoadState.Ready, rig.PollUntilSettled());
        Assert.Null(rig.Load.Error);
    }

    [Fact]
    public async Task Finish_waits_for_every_step_on_its_thread()
    {
        var rig = new Rig();
        rig.Gate.Reset();
        Assert.Equal(LoadState.Pending, rig.Load.Step());   // a player's game started it at load
        var ct = TestContext.Current.CancellationToken;
        var t = Task.Run(() => rig.Load.Finish(), ct);   // the server world starts before it is done
        await Task.Delay(50, ct);
        Assert.False(t.IsCompleted);
        rig.Gate.Set();
        Assert.Equal(LoadState.Ready, await t.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal("Twig", rig.Load.Result);
        Assert.Equal(1, rig.Reads);
    }

    [Fact]
    public void Finish_gives_up_when_the_game_data_cannot_be_read()
    {
        Assert.Equal(LoadState.NotYet, new Rig { Available = false }.Load.Finish());
        Assert.Equal(LoadState.NotYet, new Rig { ReadFacts = null }.Load.Finish());
        Assert.Equal(LoadState.Failed, new Rig { ReadThrows = new IOException("disk") }.Load.Finish());
    }
}
