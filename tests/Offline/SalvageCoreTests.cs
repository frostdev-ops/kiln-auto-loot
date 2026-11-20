// SPDX-License-Identifier: MIT
// Salvage's pure rules: the game's yield formula (as the server's Trash handler computes it), the rules for new
// pickups, what may never be salvaged, the ledger that keeps a salvage whole across a save, and the world-source book.
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline;

public sealed class SalvageFormulaTests
{
    /// <summary>The game's BalancingTable values (26430625): seed 8, creep 0.15, trash 5..20, factors 1 / 0.75 / 0.5.</summary>
    internal static readonly SalvageBalancing Game = new(8, 0.15f, 5, 20, 1f, 0.75f, 0.5f, new Guid("53345dc1-b924-465c-9d4c-f9c80674babd"));
    private static readonly SalvageFactors Default = new(1f, 0.5f);

    [Theory]
    [InlineData(1, SlotGroup.WeaponOrShield, 9)]     // 8 + 1.2 = 9.2
    [InlineData(10, SlotGroup.WeaponOrShield, 20)]
    [InlineData(20, SlotGroup.Armor, 24)]            // 32 * 0.75
    [InlineData(9, SlotGroup.Ring, 9)]               // 18.8 * 0.5 = 9.4
    [InlineData(10, SlotGroup.Ring, 10)]
    [InlineData(34, SlotGroup.WeaponOrShield, 49)]   // 48.8
    [InlineData(5, SlotGroup.Other, 14)]             // no slot factor
    public void Cost_is_the_games_upgrade_cost(int level, SlotGroup group, int cost) =>
        Assert.Equal(cost, SalvageFormula.Cost(level, group, Game, Default));

    [Fact]
    public void A_common_level_1_sword_gives_9_runes() =>
        Assert.Equal(9, SalvageFormula.Yield(1, 1, 0, SlotGroup.WeaponOrShield, Game, Default));

    [Fact]
    public void An_epic_level_20_chest_piece_gives_4_times_its_upgrade_cost() =>
        Assert.Equal(96, SalvageFormula.Yield(20, 4, 0, SlotGroup.Armor, Game, Default));

    [Fact]
    public void Unlocked_upgrades_give_back_part_of_their_runes()
    {
        // rare ring, level 10 with one upgrade: 3 x cost(9) = 27; refund round(0.5 x round(10 x 6.5)) = round(32.5) = 33
        Assert.Equal(60, SalvageFormula.Yield(10, 3, 1, SlotGroup.Ring, Game, Default));
        // legendary sword, level 34 with 4 upgrades: 5 x cost(30) = 220; 4 x round(49 x 10.1) = 1980; x 0.5 = 990
        Assert.Equal(1210, SalvageFormula.Yield(34, 5, 4, SlotGroup.WeaponOrShield, Game, Default));
    }

    [Fact]
    public void The_world_settings_scale_it()
    {
        Assert.Equal(18, SalvageFormula.Yield(1, 1, 0, SlotGroup.WeaponOrShield, Game, new SalvageFactors(2f, 0.5f)));   // 18.4
        Assert.Equal(27, SalvageFormula.Yield(10, 3, 1, SlotGroup.Ring, Game, new SalvageFactors(1f, 0f)));            // no refund
        Assert.Equal(92, SalvageFormula.Yield(10, 3, 1, SlotGroup.Ring, Game, new SalvageFactors(1f, 1f)));
    }

    [Fact]
    public void More_upgrades_than_levels_cost_at_level_1()
    {
        // level 2 with 3 unlocks: base uses max(2 - 3, 1) = 1
        var y = SalvageFormula.Yield(2, 4, 3, SlotGroup.WeaponOrShield, Game, new SalvageFactors(1f, 0f));
        Assert.Equal(4 * 9, y);
    }

    [Theory]
    [InlineData(0.5f, 1)] [InlineData(1.49f, 1)] [InlineData(2.5f, 3)] [InlineData(-0.5f, -1)] [InlineData(-1.4f, -1)]
    public void Rounding_is_half_away_from_zero(float v, int r) => Assert.Equal(r, SalvageFormula.Round(v));

    [Theory]
    [InlineData("MeleeWeapon", SlotGroup.WeaponOrShield)] [InlineData("Spell", SlotGroup.WeaponOrShield)]
    [InlineData("RangeWeapon", SlotGroup.WeaponOrShield)] [InlineData("Shield", SlotGroup.WeaponOrShield)]
    [InlineData("Tool", SlotGroup.WeaponOrShield)] [InlineData("Armour_Head", SlotGroup.Armor)]
    [InlineData("Armour_Feet", SlotGroup.Armor)] [InlineData("Armour_Ring", SlotGroup.Ring)]
    [InlineData("Backpack", SlotGroup.Other)] [InlineData("Glider", SlotGroup.Other)] [InlineData("", SlotGroup.Other)]
    public void Slot_groups_follow_the_games_switch(string slot, SlotGroup group) => Assert.Equal(group, SalvageFormula.GroupOf(slot));

    [Theory]
    [InlineData("MeleeWeapon", SalvageKind.Weapons)] [InlineData("MidRangeWeapon", SalvageKind.Weapons)]
    [InlineData("Spell", SalvageKind.Weapons)] [InlineData("Shield", SalvageKind.Shields)]
    [InlineData("Armour_UpperBody", SalvageKind.Armor)] [InlineData("Armour_Ring", SalvageKind.Rings)]
    [InlineData("Tool", SalvageKind.All)] [InlineData("Backpack", SalvageKind.All)]
    public void Rule_kinds_of_the_slots(string slot, SalvageKind kind) => Assert.Equal(kind, SalvageFormula.KindOf(slot));
}

public sealed class SalvageRuleTests
{
    [Fact]
    public void Rules_read_and_write_back()
    {
        var rules = SalvageRules.Parse("weapons:rare:20, armor:epic, rings:common!, all:uncommon:0");
        Assert.Equal(4, rules.Count);
        Assert.Equal(new SalvageRule(SalvageKind.Weapons, GearRarity.Rare, 20, true), rules[0]);
        Assert.Equal(new SalvageRule(SalvageKind.Armor, GearRarity.Epic, 0, true), rules[1]);
        Assert.Equal(new SalvageRule(SalvageKind.Rings, GearRarity.Common, 0, false), rules[2]);
        Assert.Equal("weapons:rare:20, armor:epic, rings:common!, all:uncommon", SalvageRules.Format(rules));
    }

    [Fact]
    public void Entries_that_do_not_read_are_left_out_and_at_most_six_are_kept()
    {
        Assert.Empty(SalvageRules.Parse(""));
        Assert.Empty(SalvageRules.Parse(null));
        Assert.Empty(SalvageRules.Parse("swords:rare, weapons:shiny, weapons:rare:x, :"));
        Assert.Single(SalvageRules.Parse("  WEAPONS : Rare  "));
        Assert.Equal(SalvageRules.MaxRules, SalvageRules.Parse(string.Join(",", Enumerable.Repeat("all:common", 9))).Count);
        Assert.Equal(SalvageRules.MaxLevelCap, SalvageRules.Parse("all:legendary:999")[0].MaxLevel);   // 55 + the upgrades
    }

    [Fact]
    public void A_rule_matches_its_kind_up_to_its_rarity_and_level()
    {
        var r = new SalvageRule(SalvageKind.Weapons, GearRarity.Rare, 20, true);
        Assert.True(SalvageRules.Matches(r, SalvageKind.Weapons, (int)GearRarity.Common, 5));
        Assert.True(SalvageRules.Matches(r, SalvageKind.Weapons, (int)GearRarity.Rare, 20));
        Assert.False(SalvageRules.Matches(r, SalvageKind.Weapons, (int)GearRarity.Epic, 5));
        Assert.False(SalvageRules.Matches(r, SalvageKind.Weapons, (int)GearRarity.Rare, 21));
        Assert.False(SalvageRules.Matches(r, SalvageKind.Armor, 0, 1));
        Assert.False(SalvageRules.Matches(r with { On = false }, SalvageKind.Weapons, 0, 1));
        Assert.False(SalvageRules.Matches(r, SalvageKind.Weapons, -1, 1));        // unknown rarity: never
        var all = new SalvageRule(SalvageKind.All, GearRarity.Legendary, 0, true);
        Assert.True(SalvageRules.Matches(all, SalvageKind.Rings, 4, 55));
        Assert.True(SalvageRules.Matches(all, SalvageKind.All, 0, 1));           // a tool with upgrades
        Assert.False(SalvageRules.Matches(r, SalvageKind.All, 0, 1));
    }

    [Fact]
    public void The_first_matching_rule_wins()
    {
        var rules = SalvageRules.Parse("rings:common!, rings:rare");
        Assert.Equal(rules[1], SalvageRules.FirstMatch(rules, SalvageKind.Rings, 1, 10));
        Assert.Null(SalvageRules.FirstMatch(rules, SalvageKind.Rings, 3, 10));
        Assert.Null(SalvageRules.FirstMatch([], SalvageKind.Rings, 0, 1));
    }
}

public sealed class SalvageCheckTests
{
    private static SalvageFacts Ok() => new(Place: SalvagePlace.Backpack, Equipped: false, Kept: false, Quest: false,
        HasPerks: true, HasSocket: false, GemInSocket: false, Runes: 9);

    [Fact]
    public void Plain_backpack_gear_with_a_yield_may_be_salvaged() => Assert.Equal(SalvageBlock.None, SalvageCheck.Of(Ok()));

    [Fact]
    public void What_is_never_salvaged()
    {
        Assert.Equal(SalvageBlock.Equipped, SalvageCheck.Of(Ok() with { Equipped = true }));
        Assert.Equal(SalvageBlock.Kept, SalvageCheck.Of(Ok() with { Kept = true }));
        Assert.Equal(SalvageBlock.Quest, SalvageCheck.Of(Ok() with { Quest = true }));
        Assert.Equal(SalvageBlock.NoPerks, SalvageCheck.Of(Ok() with { HasPerks = false }));
        Assert.Equal(SalvageBlock.Gem, SalvageCheck.Of(Ok() with { HasSocket = true, GemInSocket = true }));
        Assert.Equal(SalvageBlock.Gem, SalvageCheck.Of(Ok() with { HasSocket = true, GemInSocket = null }));   // unreadable
        Assert.Equal(SalvageBlock.None, SalvageCheck.Of(Ok() with { HasSocket = true, GemInSocket = false }));  // empty socket
        Assert.Equal(SalvageBlock.Elsewhere, SalvageCheck.Of(Ok() with { Place = SalvagePlace.Elsewhere }));
        Assert.Equal(SalvageBlock.None, SalvageCheck.Of(Ok() with { Place = SalvagePlace.SalvageChest }));
        Assert.Equal(SalvageBlock.NoYield, SalvageCheck.Of(Ok() with { Runes = 0 }));
        // equipped wins over everything: the reason shown is the first that applies
        Assert.Equal(SalvageBlock.Equipped, SalvageCheck.Of(Ok() with { Equipped = true, Kept = true, Quest = true }));
    }

    [Fact]
    public void Salvage_chest_names_compare_loosely()
    {
        Assert.True(SalvageCheck.IsSalvageChest(" salvage ", "Salvage"));
        Assert.True(SalvageCheck.IsSalvageChest("SALVAGE", "salvage"));
        Assert.False(SalvageCheck.IsSalvageChest("Salvage 2", "Salvage"));
        Assert.False(SalvageCheck.IsSalvageChest(null, "Salvage"));
        Assert.False(SalvageCheck.IsSalvageChest("", ""));                    // an empty name never makes one
        Assert.False(SalvageCheck.IsSalvageChest("Salvage", "  "));
    }
}

public sealed class SalvageLedgerTests
{
    [Fact]
    public void A_salvage_goes_taking_owed_paid()
    {
        var l = new SalvageLedger();
        var t = l.Begin(76561190000000001UL, "Bronze Sword", 0x41a1u, 9, nowMs: 100);
        Assert.Equal(LedgerState.Taking, l.Find(t)!.State);
        l.Taken(t);
        Assert.Equal(LedgerState.Owed, l.Find(t)!.State);
        Assert.Equal(9, l.OwedTo(76561190000000001UL).Sum(e => e.Runes));
        l.Paid(t);
        Assert.Null(l.Find(t));
        Assert.Empty(l.Entries);
    }

    [Fact]
    public void A_take_that_failed_leaves_nothing_owed()
    {
        var l = new SalvageLedger();
        var t = l.Begin(1, "x", 1, 9, 0);
        l.TakeFailed(t);
        Assert.Empty(l.Entries);
    }

    [Fact]
    public void Tickets_are_unique()
    {
        var l = new SalvageLedger();
        var a = l.Begin(1, "x", 1, 9, 0);
        var b = l.Begin(1, "x", 1, 9, 0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void After_a_load_a_take_whose_item_is_still_there_is_dropped_and_one_whose_item_is_gone_is_owed()
    {
        var l = new SalvageLedger();
        var kept = l.Begin(1, "kept", 1, 5, 0);
        var gone = l.Begin(1, "gone", 1, 7, 0);
        var owed = l.Begin(2, "owed", 1, 11, 0);
        l.Taken(owed);
        var outcome = l.ResolveAfterLoad(ticket => ticket == kept);
        Assert.Null(l.Find(kept));
        Assert.Equal(LedgerState.Owed, l.Find(gone)!.State);
        Assert.Equal(LedgerState.Owed, l.Find(owed)!.State);
        Assert.Equal((Dropped: 1, NowOwed: 1), outcome);
    }

    [Fact]
    public void The_ledger_survives_a_json_round_trip()
    {
        var l = new SalvageLedger();
        var t = l.Begin(42, "Bronze Sword", 7, 9, 5);
        l.Taken(t);
        var json = System.Text.Json.JsonSerializer.Serialize(l);
        var back = System.Text.Json.JsonSerializer.Deserialize<SalvageLedger>(json)!;
        Assert.Equal(LedgerState.Owed, back.Find(t)!.State);
        Assert.Equal(9, back.Find(t)!.Runes);
        Assert.Equal(42UL, back.Find(t)!.SteamId);
        var t2 = back.Begin(42, "y", 7, 1, 6);
        Assert.NotEqual(t, t2);
    }
}

public sealed class SalvageSourceTests
{
    [Fact]
    public void A_pickup_counts_as_from_the_world_only_right_after_a_world_source_was_looted()
    {
        var clock = new FakeClock();
        var book = new SalvageSources(clock);
        var player = new Kiln.EntityId(5);
        var chest = new Kiln.EntityId(900);
        Assert.False(book.FromWorld(player, chest));
        book.Looted(player, chest, world: true);
        Assert.True(book.FromWorld(player, chest));
        Assert.False(book.FromWorld(new Kiln.EntityId(6), chest));            // another player's loot
        clock.NowMs += SalvageSources.WindowMs + 1;
        Assert.False(book.FromWorld(player, chest));
    }

    [Fact]
    public void Sources_the_tables_do_not_know_never_count()
    {
        var clock = new FakeClock();
        var book = new SalvageSources(clock);
        var player = new Kiln.EntityId(5);
        var tomb = new Kiln.EntityId(901);
        book.Looted(player, tomb, world: false);   // a tombstone, a built chest: unknown to Auto Loot's tables
        Assert.False(book.FromWorld(player, tomb));
        Assert.False(book.FromWorld(player, Kiln.EntityId.None));
    }
}

public sealed class SalvageOwnerTests
{
    [Fact]
    public void The_steam_id_names_the_owner_when_the_game_logged_one() =>
        Assert.Equal(76561190000000001UL, SalvageOwner.Of(76561190000000001UL, 77, isHost: false));

    [Fact]
    public void Without_a_steam_id_the_account_id_does()
    {
        var a = SalvageOwner.Of(0, 77, isHost: false);
        Assert.NotEqual(0UL, a);
        Assert.NotEqual(77UL, a);                                  // never equal to a steam id
        Assert.Equal(a, SalvageOwner.Of(0, 77, isHost: false));
    }

    [Fact]
    public void The_host_of_a_hosted_world_has_an_owner_even_with_no_ids()
    {
        // a hosting game's own player is not a network peer: the game logs no steam id for it (bug in 0.2.0)
        Assert.Equal(SalvageOwner.Host, SalvageOwner.Of(0, 0, isHost: true));
        Assert.NotEqual(0UL, SalvageOwner.Host);
    }

    [Fact]
    public void Nobody_known_is_no_owner() => Assert.Equal(0UL, SalvageOwner.Of(0, 0, isHost: false));
}
