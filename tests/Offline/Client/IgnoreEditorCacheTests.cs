// SPDX-License-Identifier: MIT
// The skip-list editor's caches: a list or suggestions built while the item table was empty are built again once the
// table is filled, without the skip-list text changing.
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline.Client;

public class IgnoreEditorCacheTests
{
    [Fact]
    public void The_chosen_list_follows_the_item_table_once_it_fills()
    {
        var table = ItemTable.Empty();
        var editor = new IgnoreEditor(table);
        var before = editor.ListFor("=Apple");
        Assert.Empty(before.Ids);
        Assert.Equal(IgnoreEntryKind.Unknown, before.Classify("=Apple").Kind);
        Assert.Same(before, editor.ListFor("=Apple"));            // same text, same table: cached

        table.Fill(Fixtures.Items().All);
        var after = editor.ListFor("=Apple");
        Assert.Equal([6u], after.Ids);
        Assert.Equal(IgnoreEntryKind.Exact, after.Classify("=Apple").Kind);
        Assert.Same(after, editor.ListFor("=Apple"));
    }

    [Fact]
    public void The_suggestions_follow_the_item_table_once_it_fills()
    {
        var table = ItemTable.Empty();
        var editor = new IgnoreEditor(table);
        Assert.Empty(editor.Hits("apf", "", german: true));
        table.Fill(Fixtures.Items().All);
        Assert.Equal([6u], editor.Hits("apf", "", german: true).Select(r => r.Id).ToArray());
        Assert.Equal([1u, 2u, 3u, 8u], editor.Hits("", "Materials", german: false).Select(r => r.Id).OrderBy(i => i).ToArray());
    }
}

public class IgnoreEditorBoundsTests
{
    [Fact]
    public void The_editor_never_builds_a_list_the_server_refuses()
    {
        var editor = new IgnoreEditor(Fixtures.Items());
        var full = string.Join(", ", Enumerable.Range(0, SkipListBounds.MaxEntries).Select(i => $"=Item_{i}"));
        Assert.Null(editor.Bounded(full, full + ", =Food_Apple"));                 // one more: refused
        Assert.Equal("=Food_Apple", editor.Bounded("", "=Food_Apple"));            // fits
        var over = full + ", =a, =b";
        Assert.Equal(full + ", =a", editor.Bounded(over, full + ", =a"));          // removing from an over-long list
        Assert.Null(editor.Bounded(full, null));
    }
}

public class EnabledSettingTests
{
    [Fact]
    public void Enabled_is_read_when_a_world_starts_not_held_until_the_mod_reloads()
    {
        var prop = typeof(ServerSettings).GetProperty(nameof(ServerSettings.Enabled))!;
        Assert.DoesNotContain(prop.GetCustomAttributes(true), a => a.GetType().Name == "RequiresRestartAttribute");
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, null, false)]
    public void The_marker_shows_when_the_saved_value_differs_from_the_running_world(bool saved, bool? running, bool marked) =>
        Assert.Equal(marked, SettingSpec.RestartPending(saved, running));
}

public class IgnoreEditorUndoTests
{
    [Fact]
    public void A_cleared_list_comes_back_with_undo_even_after_the_window_was_closed()
    {
        var editor = new IgnoreEditor(Fixtures.Items());
        const string list = "=Food_Apple, #Materials, stone";
        Assert.Equal("", editor.Finish(list, "", undoing: false));          // "Really clear all 3?"
        editor.Reset();                                                       // the window closed and opened again
        Assert.Equal(list, editor.Undo.For(""));
        Assert.Equal(list, editor.Finish("", list, undoing: true));          // Undo
        Assert.Null(editor.Undo.For(list));                                  // nothing more to undo
        Assert.Null(editor.Undo.For(""));
    }

    [Fact]
    public void Undo_is_offered_only_while_the_list_is_what_the_last_change_made()
    {
        var undo = new ListUndo();
        Assert.Null(undo.For(""));
        undo.Changed("=a, =b", "=a");
        Assert.Equal("=a, =b", undo.For("=a"));
        Assert.Null(undo.For("=c"));          // a profile switch or a hand edit: no undo
        undo.Changed("=a", "=a");             // no change: the step back stays
        Assert.Equal("=a, =b", undo.For("=a"));
        undo.Changed("=a", "");
        Assert.Equal("=a", undo.For(""));     // one step only
    }

    [Fact]
    public void A_refused_change_leaves_the_undo_as_it_was()
    {
        var editor = new IgnoreEditor(Fixtures.Items());
        editor.Finish("=a", "", undoing: false);
        var full = string.Join(", ", Enumerable.Range(0, SkipListBounds.MaxEntries).Select(i => $"=Item_{i}"));
        Assert.Null(editor.Finish(full, full + ", =x", undoing: false));
        Assert.Equal("=a", editor.Undo.For(""));
    }
}
