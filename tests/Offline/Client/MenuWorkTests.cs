// SPDX-License-Identifier: MIT
// The work the F8 window posts on the title screen (Client/MenuWork), from a player's report on 0.1.6 (Nexus, 17 Nov
// 2026): Kiln runs a mod's posted work only at a world's step, so the main menu's World tab never became editable. The
// UI frame runs the queue there; a world that starts first runs what is left.
using KilnAutoLoot.Client;
using Xunit;

namespace KilnAutoLoot.Offline.ClientTests;

public sealed class MenuWorkTests
{
    private sealed class Rig
    {
        public readonly List<Action> Posted = [];   // what went to Kiln's scheduler (a world's step)
        public readonly List<Exception> Failed = [];
        public readonly MenuWork Work;

        public Rig() => Work = new MenuWork(Posted.Add, Failed.Add);

        public void WorldStep()
        {
            var due = Posted.ToList();
            Posted.Clear();
            foreach (var a in due) a();
        }
    }

    [Fact]
    public void Work_posted_on_the_title_screen_runs_at_the_next_UI_frame_in_order()
    {
        var rig = new Rig();
        var ran = new List<string>();
        rig.Work.Post(() => ran.Add("register the world's settings"));
        rig.Work.Post(() => ran.Add("set the stack size"));
        Assert.True(rig.Work.Pending);
        Assert.Empty(ran);   // nothing runs until a frame takes it
        rig.Work.Run();
        Assert.Equal(["register the world's settings", "set the stack size"], ran);
        Assert.False(rig.Work.Pending);
    }

    [Fact]
    public void Each_piece_runs_once_when_a_world_step_comes_after_the_frame()
    {
        var rig = new Rig();
        var count = 0;
        rig.Work.Post(() => count++);
        rig.Work.Run();
        rig.WorldStep();   // the drain posted along with it finds nothing left
        Assert.Equal(1, count);
    }

    [Fact]
    public void A_world_that_starts_first_runs_what_is_left()
    {
        var rig = new Rig();
        var count = 0;
        rig.Work.Post(() => count++);
        rig.Work.Post(() => count++);
        Assert.NotEmpty(rig.Posted);   // the drain is with Kiln's scheduler too
        rig.WorldStep();
        Assert.Equal(2, count);
        Assert.False(rig.Work.Pending);
    }

    [Fact]
    public void A_piece_that_throws_is_reported_and_the_rest_still_run()
    {
        var rig = new Rig();
        var ran = false;
        rig.Work.Post(() => throw new InvalidOperationException("This must run on the tick thread"));
        rig.Work.Post(() => ran = true);
        rig.Work.Run();
        Assert.True(ran);
        Assert.Equal("This must run on the tick thread", Assert.Single(rig.Failed).Message);
    }
}
