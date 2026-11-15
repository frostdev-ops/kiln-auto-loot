// SPDX-License-Identifier: MIT
// The item table: Lower, DisplayOf and the suggestions.
using KilnAutoLoot.Core;

namespace KilnAutoLoot.Offline;

public class ItemTableTests
{
    [Theory]
    [InlineData("ÄÖÜÉ", "äöüé")]
    [InlineData("ABC xyz", "abc xyz")]
    [InlineData("×", "×")]        // U+00D7 stays
    [InlineData("ÀÞ", "àþ")]      // the ends of U+00C0..U+00DE
    [InlineData("ß", "ß")]        // U+00DF is not upper case
    [InlineData("ŸŒĀ", "ŸŒĀ")]    // outside Latin-1: unchanged
    [InlineData("İI", "İi")]      // locale-independent
    public void Lower_is_ASCII_and_Latin1_only(string input, string expected) =>
        Assert.Equal(expected, ItemTable.Lower(input));

    [Fact]
    public void Columns_are_read_as_written()
    {
        var items = Fixtures.Items();
        Assert.Equal(8, items.All.Count);
        Assert.True(items.TryGet(1, out var twigs));
        Assert.Equal(Guid.Parse("b28bc647-c6b2-4415-9bef-82acfbf97353"), twigs.Guid);
        Assert.Equal(50, twigs.OriginalMaxStack);
        Assert.True(items.TryGet(5, out var hidden));
        Assert.False(hidden.Lootable);
        Assert.True(items.TryGet(6, out var apple));
        Assert.Equal("Food", apple.MenuGroup);   // empty column 7 -> column 6
        Assert.True(items.TryGet(3, out var fibre));
        Assert.True(fibre.Lootable);             // missing guid/stack columns
        Assert.Equal(Guid.Empty, fibre.Guid);
        Assert.Equal(0, fibre.OriginalMaxStack);
        Assert.True(items.TryGetByInternal("  material_t1_TWIGS ", out var byName));
        Assert.Equal(1u, byName.Id);
    }

    [Fact]
    public void Display_German_then_English_then_internal()
    {
        var items = Fixtures.Items();
        Assert.Equal("Äste", items.Display(1, true));
        Assert.Equal("Twigs", items.Display(1, false));
        Assert.Equal("Hidden", items.Display(5, true));    // no German name
        Assert.Equal("No_Names", items.Display(8, false));
        Assert.Equal("", items.Display(999, true));        // an unknown item: no notice
    }

    [Fact]
    public void Suggest_starts_with_first_then_contains_in_file_order()
    {
        var items = Fixtures.Items();
        Assert.Equal(new uint[] { 2, 1, 4, 7 }, items.Suggest("st", null, german: false).Select(r => r.Id));
        Assert.Equal(new uint[] { 2, 1, 4, 7 }, items.Suggest("ST ", null, german: true).Select(r => r.Id));
        // the display name follows the menu language
        Assert.Equal(new uint[] { 3, 6 }, items.Suggest("pf", null, german: true).Select(r => r.Id));
        Assert.Equal(new uint[] { 3, 6 }, items.Suggest("pf", null, german: false).Select(r => r.Id));
        Assert.Equal(3u, items.Suggest("pf", null, german: true)[0].Id);   // pass 1: Pflanzenfaser starts with it
    }

    [Fact]
    public void Suggest_lootable_only_group_filter_and_max()
    {
        var items = Fixtures.Items();
        Assert.Empty(items.Suggest("hidden", null, false));
        Assert.Empty(items.Suggest("", null, false));           // no query, no group
        Assert.Empty(items.Suggest("", "", false));
        Assert.Equal(new uint[] { 2 }, items.Suggest("st", "Materials", false).Select(r => r.Id).Take(1));
        Assert.DoesNotContain(items.Suggest("st", "Materials", false), r => r.Id == 7);
        Assert.Single(items.Suggest("st", null, false, max: 1));
        Assert.Empty(items.Suggest("st", null, false, max: 0));
    }

    [Fact]
    public void Suggest_with_a_group_and_no_query_sorts_umlauts_as_base_letters()
    {
        var items = Fixtures.Items();
        // German: Äste (aste), No_Names (no_names), Pflanzenfaser, Stein
        Assert.Equal(new uint[] { 1, 8, 3, 2 }, items.Suggest("", "Materials", german: true).Select(r => r.Id));
        // English: No_Names, Plant Fiber, Stone, Twigs
        Assert.Equal(new uint[] { 8, 3, 2, 1 }, items.Suggest("", "Materials", german: false).Select(r => r.Id));
        Assert.Equal("strasse", ItemTable.SortKey("Straße"));
        Assert.Equal("aouaou", ItemTable.SortKey("ÄÖÜäöü"));
    }
}
