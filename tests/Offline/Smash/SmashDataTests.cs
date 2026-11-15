// SPDX-License-Identifier: MIT
// The smash table (fixture text and Fill), and the stack and backpack rules.
using KilnAutoLoot.Server;
using Xunit;

namespace KilnAutoLoot.Offline.Smash;

public sealed class SmashTableTests
{
    [Fact]
    public void Parses_kinds_and_skips_comments_and_bad_lines()
    {
        var t = SmashTable.Parse(new StringReader(
            "# header\n" +
            "00000000-0000-0000-0000-000000000001\tsmash\tA\n" +
            "00000000-0000-0000-0000-000000000002\tstone-shared\tB\n" +
            "not-a-guid\tsmash\tC\n" +
            "00000000-0000-0000-0000-000000000003\tlater-kind\tD\n" +
            "\n"));
        Assert.Equal(2, t.Count);
        Assert.True(t.TryGet(new Guid("00000000-0000-0000-0000-000000000002"), out var k));
        Assert.Equal(SmashKind.StoneShared, k);
        Assert.False(t.TryGet(new Guid("00000000-0000-0000-0000-000000000003"), out _));
    }

    [Fact]
    public void Fill_swaps_the_content_and_word_round_trips()
    {
        var t = SmashTable.Empty;
        Assert.Equal(0, t.Count);
        var g = Guid.NewGuid();
        t.Fill([new(g, SmashKind.Tree)]);
        Assert.True(t.TryGet(g, out var k));
        Assert.Equal(SmashKind.Tree, k);
        foreach (var kind in Enum.GetValues<SmashKind>()) Assert.Equal(kind, SmashTable.ParseKind(SmashTable.WordOf(kind)));
        Assert.NotSame(SmashTable.Empty, SmashTable.Empty);   // each Data gets a table of its own
    }
}

public sealed class WorldDataRulesTests
{
    [Theory]
    [InlineData(50, 9999, 9999)]     // raised
    [InlineData(1, 9999, 1)]         // single items stay single
    [InlineData(0, 9999, 0)]
    [InlineData(20000, 9999, 20000)] // never below the game's own
    [InlineData(50, 0, 50)]          // 0 = the game's own
    [InlineData(50, 30, 50)]         // lowering never goes below the game's own
    [InlineData(50, 70000, 65535)]   // the 16-bit cap
    [InlineData(2, 2, 2)]
    public void Stack_target(int original, int wish, int expected) =>
        Assert.Equal(expected, WorldDataRules.StackTarget(original, wish));

    [Theory]
    [InlineData("Special_Backpack_T1", true)]
    [InlineData("Special_Backpack_T0", true)]
    [InlineData("special_backpack_T1", false)]
    [InlineData("Recipe_Backpack_T1", false)]
    public void Backpack_recipes_by_debug_name(string name, bool expected) =>
        Assert.Equal(expected, WorldDataRules.IsBackpackRecipe(name));

    [Theory]
    [InlineData(5, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Backpack_inputs_cost_one(int count, int expected) =>
        Assert.Equal(expected, WorldDataRules.BackpackInputCount(count));
}
