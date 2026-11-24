// SPDX-License-Identifier: MIT
using Kiln;
using Kiln.TestKit;
using KilnAutoLoot.Client;
using KilnAutoLoot.Server;

namespace KilnAutoLoot.Offline;

public sealed class SightRayOverlayTests
{
    private static readonly WorldPosition Player = new(1, 10, 2), Corpse = new(3, 9, 5);
    private static readonly SightAnswer Clear = new(Sight.Visible);

    [Fact]
    public void The_local_checkbox_defaults_off_persists_and_only_subscribes_while_hosting()
    {
        using var kiln = new KilnHarness();
        kiln.Run(() =>
        {
            var overlay = new SightRayOverlay();
            bool DrawSubscribed() => Events.Subscriptions.Any(e => e.Type == typeof(RenderFrameEvent) && e.Name == "SightRayOverlay.Draw");
            overlay.Start();
            try
            {
                Assert.False(overlay.Enabled);
                overlay.SetHosted(true);
                Assert.False(overlay.Recording);
                Assert.False(DrawSubscribed());
                overlay.SetHosted(false);

                overlay.Save(true);
                Assert.Null(overlay.SaveError);
                Assert.True(overlay.Enabled);
                Assert.False(overlay.Recording);
                Assert.False(DrawSubscribed());
                var path = Path.Combine(kiln.ConfigDirectory, "debug-client.json");
                using (var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path),
                           new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }))
                    Assert.True(saved.RootElement.GetProperty("showCorpseSightRays").GetBoolean());

                overlay.SetHosted(true);
                Assert.True(overlay.Recording);
                Assert.True(DrawSubscribed());
                overlay.Record(new(1), SightSample.Primary, Clear, Player, Corpse, null, 0);
                overlay.Save(false);
                Assert.False(overlay.Enabled);
                Assert.False(overlay.Recording);
                Assert.False(DrawSubscribed());
                Assert.Equal(0, overlay.CopyActive(new SightRayOverlay.Segment[1], 1));
            }
            finally { overlay.Stop(); }
        });
    }

    [Fact]
    public void UsesTheQueriedOffsetsAndExactHitAndKeepsSamplesDistinct()
    {
        var overlay = new SightRayOverlay();
        var hit = new WorldPosition(2, 10, 3);
        overlay.Record(new(1), SightSample.Primary, new(Sight.Hidden, new SightHit(hit, 2, 1, 2)), Player, Corpse, null, 0);
        overlay.Record(new(1), SightSample.CorpseBody, Clear, Player, Corpse, KilnGame.CorpseSightOptions, 1);
        overlay.Record(new(1), SightSample.CorpseUpper, Clear, Player, Corpse, KilnGame.CorpseUpperSightOptions, 1);
        var rows = new SightRayOverlay.Segment[3];
        Assert.Equal(3, overlay.CopyActive(rows, 1));
        Assert.All(rows, r => Assert.Equal(new WorldPosition(1, 11.5, 2), r.From));
        Assert.Equal(new WorldPosition(3, 10.5, 5), rows[0].To);
        Assert.Equal(hit, rows[0].Hit);
        Assert.Equal(new WorldPosition(3, 9.35, 5), rows[1].To);
        Assert.Equal(new WorldPosition(3, 9.65, 5), rows[2].To);
        Assert.Null(rows[1].Hit);
        Assert.Null(rows[2].Hit);
    }

    [Fact]
    public void UnknownDeferredAndInvalidResultsDoNotDraw()
    {
        var overlay = new SightRayOverlay();
        foreach (var state in new[] { Sight.Unknown, Sight.Later, Sight.Hidden })
            overlay.Record(new(1), SightSample.Primary, new(state), Player, Corpse, null, 0);
        overlay.Record(new(1), SightSample.Primary, Clear, Player, Player, null, 0);
        overlay.Record(new(1), SightSample.Primary, Clear, Player, Corpse with { Y = double.NaN }, null, 0);
        Assert.Equal(0, overlay.CopyActive(new SightRayOverlay.Segment[1], 0));
    }

    [Fact]
    public void ReplacesTheSameSampleAndExpiresOrClearsRatherThanDrawingStaleRays()
    {
        var overlay = new SightRayOverlay();
        overlay.Record(new(1), SightSample.Primary, Clear, Player, Corpse, null, 0);
        overlay.Record(new(1), SightSample.Primary, Clear, Player with { X = 2 }, Corpse, null, 1);
        var rows = new SightRayOverlay.Segment[2];
        Assert.Equal(1, overlay.CopyActive(rows, SightRayOverlay.VisibleMs));
        Assert.Equal(2, rows[0].From.X);
        Assert.Equal(0, overlay.CopyActive(rows, SightRayOverlay.VisibleMs + 1));
        overlay.Clear();
        Assert.Equal(0, overlay.CopyActive(rows, 1));
    }

    [Fact]
    public void RetainsAtMostTheBoundedNumberOfRecentSegments()
    {
        var overlay = new SightRayOverlay();
        for (uint i = 1; i <= SightRayOverlay.Capacity + 1; i++)
            overlay.Record(new(i), SightSample.Primary, Clear, Player, Corpse, null, i);
        var rows = new SightRayOverlay.Segment[SightRayOverlay.Capacity + 1];
        Assert.Equal(SightRayOverlay.Capacity, overlay.CopyActive(rows, 100));
        Assert.DoesNotContain(rows, r => r.Target.Value == 1);
        Assert.Contains(rows, r => r.Target.Value == SightRayOverlay.Capacity + 1);
    }
}
