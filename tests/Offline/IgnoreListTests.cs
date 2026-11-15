// SPDX-License-Identifier: MIT
// The ignore list: matching, and the editor.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class IgnoreListTests
{
    [Fact]
    public void Bare_word_matches_internal_English_or_German_substring_any_case()
    {
        Assert.Equal(new uint[] { 1 }, Fixtures.Ignore("twig").Ids.Order());
        Assert.Equal(new uint[] { 1 }, Fixtures.Ignore("ÄSTE").Ids.Order());          // German, Lower on Ä
        Assert.Equal(new uint[] { 2, 7 }, Fixtures.Ignore("stein").Ids.Order());      // Stein, Feuerstein
        Assert.Equal(new uint[] { 5 }, Fixtures.Ignore("hidden").Ids.Order());        // not lootable: matched too
    }

    [Fact]
    public void Exact_entries_match_a_whole_name_and_unknown_ones_are_kept()
    {
        var l = Fixtures.Ignore(" =twigs , =Material_T1_Stone,=Feuerstein, =twig, =  ");
        Assert.Equal(new uint[] { 1, 2, 7 }, l.Ids.Order());
        Assert.Equal(new[] { "=twigs", "=Material_T1_Stone", "=Feuerstein", "=twig", "=" }, l.Entries);
        Assert.Equal(new[] { "=twig", "=" }, l.UnknownEntries);
        Assert.True(l.IsUnknown("=twig"));
        Assert.False(l.IsUnknown("=TWIGS"));
        Assert.False(l.IsUnknown("twig"));       // bare words are never unknown
        Assert.False(l.IsUnknown("#Nothing"));   // nor groups
    }

    [Fact]
    public void Group_entries_match_the_menu_group_any_case()
    {
        Assert.Equal(new uint[] { 1, 2, 3, 8 }, Fixtures.Ignore("#materials").Ids.Order());
        Assert.Equal(new uint[] { 6 }, Fixtures.Ignore("# Food").Ids.Order());     // column 6 when 7 is empty; trimmed
        var none = Fixtures.Ignore("#Nothing");
        Assert.Empty(none.Ids);
        Assert.Empty(none.UnknownEntries);
    }

    [Fact]
    public void Empty_entries_are_skipped()
    {
        var l = Fixtures.Ignore(" , ,,\t");
        Assert.Empty(l.Entries);
        Assert.Empty(l.Ids);
        Assert.Empty(Fixtures.Ignore("").Entries);
    }

    [Fact]
    public void IsChosen_compares_without_the_equals_sign_ASCII_case_insensitive()
    {
        var items = Fixtures.Items();
        items.TryGet(1, out var twigs);
        items.TryGet(2, out var stone);
        Assert.True(new IgnoreList("=MATERIAL_T1_TWIGS", items).IsChosen(twigs));
        Assert.True(new IgnoreList("material_t1_twigs", items).IsChosen(twigs));   // a bare word equal to the name
        Assert.False(new IgnoreList("=Twigs", items).IsChosen(twigs));             // English name is not the internal
        Assert.False(new IgnoreList("twig", items).IsChosen(twigs));
        Assert.False(new IgnoreList("=Material_T1_Twigs", items).IsChosen(stone));
    }

    [Fact]
    public void Editing_joins_entries_with_comma_space()
    {
        var items = Fixtures.Items();
        items.TryGet(1, out var twigs);
        items.TryGet(2, out var stone);
        var l = new IgnoreList("twig,,  =Material_T1_Stone", items);
        Assert.Equal("twig, =Material_T1_Stone, =Material_T1_Twigs", l.WithExact(twigs));
        Assert.Equal("twig, =Material_T1_Stone", l.WithExact(stone));              // already chosen: unchanged
        Assert.Equal("twig, =Material_T1_Stone, #Keys", l.WithGroup("Keys"));
        Assert.Equal("twig", l.Without(" =material_t1_stone "));
        Assert.Equal("=Material_T1_Stone", l.WithoutAt(0));
        Assert.Equal("twig", l.Toggled(stone));
        Assert.Equal("twig, =Material_T1_Stone, =Material_T1_Twigs", l.Toggled(twigs));
        var g = new IgnoreList("#keys", items);
        Assert.True(g.IsGroupChosen("Keys"));
        Assert.Equal("#keys", g.WithGroup("Keys"));
        Assert.Equal("", g.ToggledGroup("Keys"));
        Assert.Equal("#keys, #Food", g.ToggledGroup("Food"));
    }

    [Fact]
    public void Classify_follows_the_chosen_list_rules()
    {
        var l = Fixtures.Ignore("");
        Assert.Equal(IgnoreEntryKind.Group, l.Classify("#materials").Kind);
        Assert.Equal("Materials", l.Classify("#materials").GroupKey);
        Assert.Equal(IgnoreEntryKind.NamePart, l.Classify("#Nope").Kind);
        Assert.Equal(IgnoreEntryKind.Unknown, l.Classify("=nope").Kind);
        var item = l.Classify("=Material_T1_Twigs");
        Assert.Equal(IgnoreEntryKind.Item, item.Kind);
        Assert.Equal(1u, item.Item!.Id);
        Assert.Equal(IgnoreEntryKind.Item, l.Classify("flint").Kind);    // a bare word equal to an internal name
        var exact = l.Classify("=Twigs");
        Assert.Equal(IgnoreEntryKind.Exact, exact.Kind);
        Assert.Equal("Twigs", exact.Text);
        Assert.Equal(IgnoreEntryKind.NamePart, l.Classify("twig").Kind);
    }
}
