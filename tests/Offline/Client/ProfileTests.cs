// SPDX-License-Identifier: MIT
// Profiles (the book, its file, the built-ins), the carry-limit rows, gather focus notes and the profile key names.
using KilnAutoLoot.Client;
using KilnAutoLoot.Core;
using Xunit;

namespace KilnAutoLoot.Offline.Client;

internal static class GameItemNames
{
    /// <summary>The items the built-in profiles name, as the game (build 26430253) has them: id, internal name, English
    /// name, category, GUID, stack size. A built-in entry that is not here is a typo or an item the game does not have.</summary>
    public const string Text =
        "1798662696\tMaterial_T1_Twigs\tTwigs\t\t1\tMaterials\t\t8c2b7ff9-52b3-4b03-8e33-df22d3e40c90\t250\n" +
        "722268629\tMaterial_T1_Wood_Logs\tWood Logs\t\t1\tMaterials\t\t3f4093ae-75ed-4e5d-bd0b-784ba83b88f5\t250\n" +
        "1320899587\tMaterial_T1_Wood_Logs_Dead\tShroud Wood\t\t1\tMaterials\t\t30223f80-743d-45f7-b89c-5e7e9d0c55d0\t250\n" +
        "1765656585\tMaterial_T2_Wood_Hardwood\tHardwood\t\t1\tMaterials\t\t18272f9d-0021-4149-9776-4cfcdf2394a9\t250\n" +
        "2955233340\tMaterial_T4_Wood_Logs_Palm\tPalm Wood Logs\t\t1\tMaterials\t\tdf571461-dc7b-4ea5-a4d4-d305106505bc\t250\n" +
        "1475408789\tMaterial_T5_Wood_Logs_Pine\tConifer Logs\t\t1\tMaterials\t\t432f909f-45c9-4e32-9773-d4708e927524\t250\n" +
        "1381084162\tMaterial_T6_Wood_Logs_Bamboo\tBamboo Logs\t\t1\tMaterials\t\t4fbdd564-096b-4c56-be91-7b684196d425\t250\n" +
        "1283252179\tMaterial_T6_Wood_Logs_Tropical\tTropical Wood\t\t1\tMaterials\t\tbb19e0b5-a468-4fb7-b7d8-f17bafb1332b\t250\n" +
        "631520303\tBuild_TerrainMaterial_T1_Stone\tStone\t\t1\tMaterials\t\t8870fa83-04e6-4b5a-ac2a-ea13205ce469\t1000\n" +
        "1586398842\tMaterial_T1_Stone_Flint\tFlintstone\t\t1\tMaterials\t\t91d3f063-2bcb-4a35-b257-5b930df563db\t1000\n" +
        "1181280513\tBuild_TerrainMaterial_T2_Stone_DeepForest\tRevelwood Stone\t\t1\tMaterials\t\ta95fac55-d6db-43b8-877c-05fe389effac\t1000\n" +
        "760251531\tBuild_TerrainMaterial_T2_Stone_Talltree\tBlackmire Stone\t\t1\tMaterials\t\tcb1c819a-fe73-4e5c-afec-fdb505b401f4\t1000\n" +
        "3445636557\tBuild_TerrainMaterial_T3_Stone_Steppes_Limestone\tLimestone\t\t1\tMaterials\t\t584bbcbe-8ed6-4a5f-97dd-409f9660214e\t1000\n" +
        "1401630464\tBuild_TerrainMaterial_T4_Sandstone\tSandstone\t\t1\tMaterials\t\t2f9dc768-2081-481c-a700-945c864eb6b0\t1000\n" +
        "3865976041\tBuild_TerrainMaterial_T5_Granite\tGranite\t\t1\tMaterials\t\t83101a9d-572a-4427-bdb1-817811f770fb\t1000\n" +
        "1853942795\tBuild_TerrainMaterial_T6_Wetlands_Stone_Limestone_Shells\tShell Limestone\t\t1\tMaterials\t\tfefff736-a7ae-4865-b83a-bf4c99992160\t1000\n" +
        "885155884\tBuild_TerrainMaterial_T7_AncientLand_Stone\tCinder Rock\t\t1\tMaterials\t\tdea7f8df-4d3c-4577-9744-2a22e34d1ced\t1000\n" +
        "2851443310\tBuild_TerrainMaterial_T7_AncientLand_Stone_Basalt\tBasalt\t\t1\tMaterials\t\t62e33c3d-041f-4435-b58f-de8c58184cbd\t1000\n" +
        "731732847\tMaterial_T2_Ore_Copper\tCopper Ore\t\t1\tMaterials\t\tc29a5ecb-1c7a-4dd5-84fc-2fff8cdf0ada\t250\n" +
        "3749540900\tMaterial_T3_Ore_Tin\tTin Ore\t\t1\tMaterials\t\t767a4256-c082-4a6e-a6e5-129a44058063\t250\n" +
        "455684957\tMaterial_T4_Ore_Iron\tIron Ore\t\t1\tMaterials\t\t9a58bec9-ed97-4730-a7e6-a6cd5eec0af0\t250\n" +
        "3222775602\tMaterial_T5_Silver_Ore\tSilver Ore\t\t1\tMaterials\t\t29cfa47f-d501-4aa9-8895-2bff58d3eee9\t250\n" +
        "2715107589\tMaterial_T6_Gold_Ore\tGold Ore\t\t1\tMaterials\t\t68807927-26ec-4791-b6d5-6d09198159be\t250\n" +
        "2035945146\tMaterial_T7_Celesthium_Ore\tCelestium Ore\t\t1\tMaterials\t\tcf6e47f3-9e1b-482e-aaf6-3b041d95db5d\t250\n" +
        "2819734894\tMaterial_T4_Sulfur\tSulfur\t\t1\tMaterials\t\t99bb8b93-9959-474f-8d9c-64c10cb1973d\t250\n" +
        "2348656032\tMaterial_T5_Coal\tCoal\t\t1\tMaterials\t\t75475b1e-e17c-449c-8aef-28df15ae5e76\t250\n" +
        "";

    public static ItemTable Items() => ItemTable.Parse(new StringReader(Text));
}

public class ProfileBookTests
{
    private static ProfileBook Fresh() => ProfileBook.Seed(new PersonalSettings(), "My settings");

    [Fact]
    public void First_start_seeds_the_built_ins_with_everything_active()
    {
        var book = Fresh();
        Assert.Equal(BuiltInProfiles.Ids, book.Snapshot.Profiles.Select(p => p.Id));
        Assert.Equal(BuiltInProfiles.Everything, book.Active.Id);
        Assert.All(book.Snapshot.Profiles, p => Assert.True(p.BuiltIn));
        Assert.Equal("Wood & sticks", book.Snapshot.Profiles[1].Display(false));
        Assert.Equal("Holz & Stöcke", book.Snapshot.Profiles[1].Display(true));
    }

    [Fact]
    public void First_start_keeps_settings_the_player_already_chose_as_a_profile()
    {
        var mine = new PersonalSettings { Radius = 30, Fish = true };
        var book = ProfileBook.Seed(mine, "Meine Einstellungen");
        Assert.Equal("mine", book.Active.Id);
        Assert.Equal("Meine Einstellungen", book.Active.Display(false));
        Assert.Equal(30f, book.SettingsOf("mine")!.Radius);
        Assert.Equal(7, book.Snapshot.Profiles.Count);
        Assert.Equal(new PersonalSettings().Radius, book.SettingsOf(BuiltInProfiles.Everything)!.Radius);
        // only the language set is still the defaults
        Assert.Equal(BuiltInProfiles.Everything, ProfileBook.Seed(new PersonalSettings { Language = "de" }, "x").Active.Id);
    }

    [Fact]
    public void The_book_round_trips_through_its_file_form()
    {
        var book = Fresh();
        var id = book.Add("Fishing", "Ctrl+1", new PersonalSettings { Fish = true, ItemFilter = FilterMode.Only, OnlyList = "#Materials",
            MinGearRarity = GearRarity.Epic, HeightAbove = 0, KeepFreeSlots = 3, CarryLimits = "=Material_T1_Twigs:500" })!;
        book.Rename(BuiltInProfiles.Plunder, "Raid", null);
        var json = book.ToJson();
        Assert.Contains("\"format\": \"kiln-auto-loot-profiles\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"itemFilter\": \"only\"", json);   // enums by name: the file stays readable
        var back = ProfileBook.FromJson(json)!;
        Assert.Equal(book.Snapshot.Profiles, back.Snapshot.Profiles);
        Assert.Equal(id, back.Active.Id);
        Assert.Empty(SettingValues.Changed(book.SettingsOf(id)!, back.SettingsOf(id)!));
        Assert.Equal(json, back.ToJson());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"format\":\"something-else\",\"profiles\":[]}")]
    [InlineData("{\"format\":\"kiln-auto-loot-profiles\",\"profiles\":[{\"name\":\"no id\"}]}")]
    public void A_file_that_is_not_ours_reads_as_nothing(string json) => Assert.Null(ProfileBook.FromJson(json));

    [Fact]
    public void A_damaged_entry_is_dropped_or_read_as_defaults()
    {
        var json = "{\"format\":\"kiln-auto-loot-profiles\",\"version\":7,\"active\":\"gone\",\"profiles\":[" +
                   "{\"id\":\"A b!\",\"name\":\"First\",\"key\":\"F7\",\"settings\":{\"radius\":\"lots\"}}," +
                   "{\"id\":\"a-b\",\"name\":\"Twice\"}," +
                   "{\"id\":\"wood\",\"key\":\"f7\",\"settings\":{\"itemFilter\":1}}]}";
        var book = ProfileBook.FromJson(json)!;
        Assert.Equal(["ab", "a-b", "wood"], book.Snapshot.Profiles.Select(p => p.Id));
        Assert.Equal("ab", book.Active.Id);                                    // unknown active = the first
        Assert.Equal(15f, book.SettingsOf("ab")!.Radius);                     // bad settings = defaults
        Assert.Null(book.Snapshot.Profiles[2].Key);                            // a key that is taken already is dropped
        Assert.Equal(FilterMode.Only, book.SettingsOf("wood")!.ItemFilter);   // enums as numbers read too
    }

    [Fact]
    public void Switching_puts_the_profile_in_use_but_keeps_the_language()
    {
        var book = Fresh();
        var inUse = new PersonalSettings { Language = "de" };
        var next = book.Switch(BuiltInProfiles.Wood, inUse)!;
        Assert.Equal(BuiltInProfiles.Wood, book.Active.Id);
        Assert.Equal(FilterMode.Only, next.ItemFilter);
        Assert.Equal("de", next.Language);
        Assert.Null(book.Switch("nope", inUse));
        Assert.Equal(BuiltInProfiles.Wood, book.Active.Id);
        Assert.Equal(BuiltInProfiles.Stone, book.NextId());
        book.Switch(BuiltInProfiles.Treasure, inUse);
        Assert.Equal(BuiltInProfiles.Everything, book.NextId());   // wraps round
    }

    [Fact]
    public void A_change_in_use_or_a_hand_edit_goes_into_the_active_profile_only()
    {
        var book = Fresh();
        book.Switch(BuiltInProfiles.Plants, new PersonalSettings());
        var edited = book.SettingsOf(BuiltInProfiles.Plants)!;
        Assert.False(book.UpdateActive(edited));                  // nothing changed: nothing to save
        edited.Radius = 42;
        Assert.True(book.UpdateActive(edited));
        Assert.Equal(42f, book.SettingsOf(BuiltInProfiles.Plants)!.Radius);
        Assert.Equal(15f, book.SettingsOf(BuiltInProfiles.Everything)!.Radius);
        edited.Language = "en";
        Assert.False(book.UpdateActive(edited));                  // the language is not part of a profile
    }

    [Fact]
    public void New_profiles_start_from_the_settings_in_use_and_become_active()
    {
        var book = Fresh();
        var id = book.Add("  Sticks  ", null, new PersonalSettings { FillCarriedStacks = true });
        Assert.Equal(id, book.Active.Id);
        Assert.Equal("Sticks", book.Active.Display(false));
        Assert.True(book.SettingsOf(id!)!.FillCarriedStacks);
        Assert.NotEqual(id, book.Add("Two", null, new PersonalSettings()));
        Assert.Null(book.Add("   ", null, new PersonalSettings()));
        var longId = book.Add(new string('x', 100), null, new PersonalSettings())!;
        Assert.Equal(ProfileBook.MaxNameLength, book.Active.Display(false).Length);
        Assert.Equal(longId, ProfileKeys.SafeId(longId));                  // the id is safe for the file and a binding
        Assert.NotNull(book.Add("Keyed", "F7", new PersonalSettings()));
        Assert.Null(book.Add("Other", "f7", new PersonalSettings()));    // that key is taken
    }

    [Fact]
    public void An_id_is_never_used_again_after_a_delete()
    {
        // the id names the Kiln binding, and Kiln keeps a rebound key for an id: a new profile must not get the old one
        var book = Fresh();
        var first = book.Add("Sticks", "F7", new PersonalSettings())!;
        Assert.True(book.Delete(first, new PersonalSettings(), out _));
        var second = book.Add("Sticks", "F7", new PersonalSettings())!;
        Assert.NotEqual(first, second);
        Assert.NotEqual(ProfileKeys.BindingId(first), ProfileKeys.BindingId(second));
        Assert.StartsWith("p-", second);
        Assert.Equal(second, ProfileKeys.SafeId(second));
    }

    [Fact]
    public void Rename_rules()
    {
        var book = Fresh();
        Assert.True(book.Rename(BuiltInProfiles.Wood, "Logs", "Ctrl+2"));
        Assert.Equal("Logs", book.Snapshot.Profiles[1].Name);
        Assert.Equal("Ctrl+2", book.Snapshot.Profiles[1].Key);
        Assert.False(book.Rename(BuiltInProfiles.Stone, "Rocks", "ctrl + 2"));   // that key is the wood profile's
        Assert.False(book.Rename(BuiltInProfiles.Stone, "", null));
        Assert.False(book.Rename("nope", "x", null));
        Assert.True(book.Rename(BuiltInProfiles.Wood, "Holz & Stöcke", "None"));  // its own name again: the text follows the language
        Assert.Null(book.Snapshot.Profiles[1].Name);
        Assert.Null(book.Snapshot.Profiles[1].Key);
        Assert.Equal("Wood & sticks", book.Snapshot.Profiles[1].Display(false));
        book.Rename(BuiltInProfiles.Stone, "Rocks", "F7");
        Assert.Equal(BuiltInProfiles.Stone, book.KeyOwner("f7", null)?.Id);
        Assert.Null(book.KeyOwner("F7", BuiltInProfiles.Stone));          // its own key is not taken from itself
        Assert.Null(book.KeyOwner("", null));
    }

    [Fact]
    public void Delete_never_the_last_and_the_active_one_hands_over()
    {
        var book = Fresh();
        book.Switch(BuiltInProfiles.Stone, new PersonalSettings());
        Assert.True(book.Delete(BuiltInProfiles.Stone, new PersonalSettings { Language = "en" }, out var apply));
        Assert.Equal(BuiltInProfiles.Everything, book.Active.Id);
        Assert.NotNull(apply);
        Assert.Equal("en", apply!.Language);
        Assert.True(book.Delete(BuiltInProfiles.Plunder, new PersonalSettings(), out apply));
        Assert.Null(apply);                                       // not the active one: nothing changes in use
        foreach (var id in book.Snapshot.Profiles.Skip(1).Select(p => p.Id).ToList()) Assert.True(book.Delete(id, new PersonalSettings(), out _));
        Assert.Single(book.Snapshot.Profiles);
        Assert.False(book.Delete(book.Active.Id, new PersonalSettings(), out _));
        Assert.False(book.Delete("nope", new PersonalSettings(), out _));
    }

    [Fact]
    public void Reset_puts_a_built_in_back_and_only_a_built_in()
    {
        var book = Fresh();
        book.Switch(BuiltInProfiles.Treasure, new PersonalSettings());
        book.UpdateActive(new PersonalSettings { Radius = 3 });
        book.Rename(BuiltInProfiles.Treasure, "Loot", "F7");
        Assert.True(book.Reset(BuiltInProfiles.Treasure, new PersonalSettings(), out var apply));
        Assert.Equal(GearRarity.Rare, apply!.MinGearRarity);
        Assert.Equal(GearRarity.Rare, book.SettingsOf(BuiltInProfiles.Treasure)!.MinGearRarity);
        Assert.Null(book.Active.Name);
        Assert.Equal("F7", book.Active.Key);                      // the key stays
        var id = book.Add("Mine", null, new PersonalSettings())!;
        Assert.False(book.Reset(id, new PersonalSettings(), out _));
    }
}

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kal-profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void First_start_seeds_then_the_saved_file_is_read_back()
    {
        var store = new ProfileStore(_dir);
        var book = store.Load(new PersonalSettings(), "Mine", out var seeded, out var problem);
        Assert.True(seeded);
        Assert.Null(problem);
        var id = book.Add("Sticks", "Ctrl+3", new PersonalSettings { FillCarriedStacks = true });
        Assert.Null(store.Save(book));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
        var again = store.Load(new PersonalSettings(), "Mine", out seeded, out problem);
        Assert.False(seeded);
        Assert.Null(problem);
        Assert.Equal(book.Snapshot.Profiles, again.Snapshot.Profiles);
        Assert.Equal(id, again.Active.Id);
    }

    [Fact]
    public void A_property_written_twice_at_the_top_makes_the_file_unusable()
    {
        Directory.CreateDirectory(_dir);
        var store = new ProfileStore(_dir);
        var text = "{\"format\":\"kiln-auto-loot-profiles\",\"version\":1,\"active\":\"a\",\"active\":\"b\"," +
                   "\"profiles\":[{\"id\":\"a\",\"name\":\"A\"}]}";
        File.WriteAllText(store.FilePath, text);
        Assert.Null(ProfileBook.FromJson(text));
        var book = store.Load(new PersonalSettings(), "Mine", out var seeded, out var problem);
        Assert.True(seeded);
        Assert.NotNull(problem);
        Assert.Equal(text, File.ReadAllText(store.FilePath + ".bad"));
        Assert.Equal(BuiltInProfiles.Ids, book.Snapshot.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void A_profile_with_a_property_written_twice_is_skipped_and_the_others_stay()
    {
        var text = "{\"format\":\"kiln-auto-loot-profiles\",\"version\":1,\"active\":\"b\",\"profiles\":[" +
                   "{\"id\":\"a\",\"id\":\"x\",\"name\":\"A\"},{\"id\":\"b\",\"name\":\"B\",\"name\":\"C\"},{\"id\":\"c\",\"name\":\"Kept\"}]}";
        var book = ProfileBook.FromJson(text)!;
        Assert.Equal(["c"], book.Snapshot.Profiles.Select(p => p.Id));
        Assert.Equal("c", book.Active.Id);
    }

    [Fact]
    public void A_broken_file_is_kept_aside_and_the_built_ins_start_again()
    {
        Directory.CreateDirectory(_dir);
        var store = new ProfileStore(_dir);
        File.WriteAllText(store.FilePath, "{ half a file");
        var book = store.Load(new PersonalSettings(), "Mine", out var seeded, out var problem);
        Assert.True(seeded);
        Assert.NotNull(problem);
        Assert.Equal("{ half a file", File.ReadAllText(store.FilePath + ".bad"));
        Assert.Equal(BuiltInProfiles.Everything, book.Active.Id);
    }
}

public class BuiltInProfileTests
{
    [Fact]
    public void Every_only_list_entry_is_an_item_of_the_game()
    {
        var items = GameItemNames.Items();
        foreach (var id in BuiltInProfiles.Ids)
        {
            var s = BuiltInProfiles.Settings(id);
            var list = new IgnoreList(s.OnlyList, items);
            Assert.Empty(list.UnknownEntries);
            Assert.All(list.Entries, e => Assert.True(e[0] is '=' or '#', $"{id}: {e} is a name part"));
            Assert.Equal(list.Entries.Count, list.Ids.Count);             // one item per entry
            Assert.True(SkipListBounds.Within(s.OnlyList), id);
        }
        Assert.Contains("=Material_T1_Twigs", BuiltInProfiles.WoodItems);
        Assert.Contains("=Material_T1_Stone_Flint", BuiltInProfiles.StoneItems);
    }

    [Fact]
    public void Each_built_in_does_what_its_name_says()
    {
        Assert.Empty(SettingValues.Changed(new PersonalSettings(), BuiltInProfiles.Settings(BuiltInProfiles.Everything)));
        var wood = BuiltInProfiles.Settings(BuiltInProfiles.Wood);
        Assert.Equal(FilterMode.Only, wood.ItemFilter);
        Assert.True(wood.Plants && wood.GroundLoot);
        var stone = BuiltInProfiles.Settings(BuiltInProfiles.Stone);
        Assert.Equal(FilterMode.Only, stone.ItemFilter);
        Assert.True(stone.Stones && stone.Smash && stone.GroundLoot);
        var plants = BuiltInProfiles.Settings(BuiltInProfiles.Plants);
        Assert.True(plants.Plants && plants.Crops);
        Assert.False(plants.GroundLoot || plants.Corpses || plants.Chests || plants.Smash || plants.Fish || plants.Collectibles);
        var plunder = BuiltInProfiles.Settings(BuiltInProfiles.Plunder);
        Assert.True(plunder.Corpses && plunder.Chests);
        Assert.False(plunder.GroundLoot || plunder.Plants || plunder.Crops || plunder.Smash);
        var treasure = BuiltInProfiles.Settings(BuiltInProfiles.Treasure);
        Assert.True(treasure.Corpses && treasure.Chests);
        Assert.Equal(GearRarity.Rare, treasure.MinGearRarity);
        Assert.Throws<ArgumentException>(() => BuiltInProfiles.Settings("nope"));
    }
}

public class CarryLimitRowsTests
{
    [Fact]
    public void Rows_round_trip_with_internal_names_and_group_keys()
    {
        var raw = "=Material_T1_Twigs:500, #Materials:2000";
        var rows = CarryLimitRows.Parse(raw);
        Assert.Equal([new CarryLimit("=Material_T1_Twigs", 500), new CarryLimit("#Materials", 2000)], rows);
        Assert.Equal(raw, CarryLimitRows.Write(rows));
        Assert.Equal(raw, CarryLimitRows.Write(CarryLimitRows.Parse(" =Material_T1_Twigs : 500 ,#Materials:2000,, ")));
        Assert.Empty(CarryLimitRows.Parse(""));
        Assert.Empty(CarryLimitRows.Parse(null));
    }

    [Fact]
    public void Pieces_that_are_not_understood_are_kept_as_written()
    {
        const string raw = "sticks, =Stone:lots, =:5, =Flint:0, wood:300, =Material_T1_Twigs:50";
        var rows = CarryLimitRows.Parse(raw);
        Assert.Equal([false, false, false, true, true, true], rows.Select(r => r.Understood));
        Assert.Equal(new CarryLimit("=Flint", 0), rows[3]);             // 0 = carry none
        Assert.Equal(new CarryLimit("wood", 300), rows[4]);             // a name part typed by hand
        Assert.Equal(raw, CarryLimitRows.Write(rows));
        Assert.Equal(raw.Replace("Twigs:50", "Twigs:60"), CarryLimitRows.WithAmount(raw, 5, 60));
        Assert.Equal(raw, CarryLimitRows.WithAmount(raw, 0, 60));      // a bad row has no number
    }

    [Fact]
    public void Adding_removing_and_changing_rows()
    {
        var items = GameItemNames.Items();
        Assert.True(items.TryGetByInternal("Material_T1_Twigs", out var twigs));
        var raw = CarryLimitRows.With("", CarryLimitRows.ItemEntry(twigs), 250);
        Assert.Equal("=Material_T1_Twigs:250", raw);
        raw = CarryLimitRows.With(raw, CarryLimitRows.GroupEntry("Materials"), 2000);
        Assert.Equal("=Material_T1_Twigs:250, #Materials:2000", raw);
        Assert.Equal("=Material_T1_Twigs:900, #Materials:2000", CarryLimitRows.With(raw, "=material_t1_twigs", 900));   // the row is updated
        Assert.True(CarryLimitRows.Has(raw, "#materials"));
        Assert.Equal("#Materials:2000", CarryLimitRows.WithoutAt(raw, 0));
        Assert.Equal(raw, CarryLimitRows.WithoutAt(raw, 5));
        Assert.Equal("=Material_T1_Twigs:" + CarryLimitRows.MaxAmount + ", #Materials:2000", CarryLimitRows.WithAmount(raw, 0, int.MaxValue));
    }

    [Theory]
    [InlineData("500", 500)] [InlineData(" 12 ", 12)] [InlineData("0", 0)] [InlineData("99999999999999999999", CarryLimitRows.MaxAmount)]
    [InlineData("1000000", CarryLimitRows.MaxAmount)]
    public void Amounts_are_clamped(string text, int expected) => Assert.Equal(expected, CarryLimitRows.ParseAmount(text));

    [Theory]
    [InlineData("")] [InlineData("-5")] [InlineData("1.5")] [InlineData("ten")]
    public void Not_an_amount(string text) => Assert.Null(CarryLimitRows.ParseAmount(text));

    [Fact]
    public void The_editor_keeps_to_the_skip_list_bounds()
    {
        var editor = new CarryLimitEditor(ItemTable.Empty());
        var full = string.Join(", ", Enumerable.Range(0, SkipListBounds.MaxEntries).Select(i => $"=Item{i}:5"));
        Assert.True(SkipListBounds.Within(full));
        Assert.Null(editor.Bounded(full, CarryLimitRows.With(full, "=OneMore", 5)));
        Assert.NotNull(editor.Bounded(full, CarryLimitRows.WithoutAt(full, 0)));
        Assert.Null(editor.Bounded(full, full));                   // no change: nothing to save
    }
}

public class FocusNoteTests
{
    [Fact]
    public void Turning_on_and_off_gives_one_note_each()
    {
        var f = new FocusNotes();
        Assert.Equal(FocusNoteKind.None, f.OnAnswer(0).Kind);      // the first answer after joining
        f.Asked();
        Assert.Equal(new FocusNote(FocusNoteKind.On, 7), f.OnAnswer(7));
        Assert.Equal(FocusNoteKind.None, f.OnAnswer(7).Kind);      // the same again (a settings answer)
        Assert.Equal(7u, f.Current);
        f.Asked();
        Assert.Equal(new FocusNote(FocusNoteKind.Off, 7), f.OnAnswer(0));
        Assert.Equal(0u, f.Current);
    }

    [Fact]
    public void A_press_the_server_answers_with_none_gives_the_hint()
    {
        var f = new FocusNotes();
        f.Asked();
        Assert.Equal(FocusNoteKind.Hint, f.OnAnswer(0).Kind);      // even as the first answer
        Assert.Equal(FocusNoteKind.None, f.OnAnswer(0).Kind);      // only once per press
        f.Asked();
        Assert.Equal(FocusNoteKind.Hint, f.OnAnswer(0).Kind);
    }

    [Fact]
    public void A_focus_already_on_when_joining_is_shown_without_a_note_and_forget_starts_again()
    {
        var f = new FocusNotes();
        Assert.Equal(FocusNoteKind.None, f.OnAnswer(9).Kind);
        Assert.Equal(9u, f.Current);
        Assert.Equal(new FocusNote(FocusNoteKind.On, 4), f.OnAnswer(4));   // changed by the server: still a note
        f.Forget();
        Assert.Null(f.Current);
        Assert.Equal(FocusNoteKind.None, f.OnAnswer(4).Kind);
    }
}

public class ProfileKeyTests
{
    [Theory]
    [InlineData(null, null)] [InlineData("", null)] [InlineData("  ", null)] [InlineData("None", null)] [InlineData("none", null)]
    [InlineData(" F7 ", "F7")] [InlineData("ctrl + 1", "ctrl+1")] [InlineData("Shift+Alt+F2", "Shift+Alt+F2")]
    public void Key_texts_are_normalized(string? text, string? expected) => Assert.Equal(expected, ProfileKeys.Normalize(text));

    [Fact]
    public void Same_key_whatever_the_case_spaces_or_modifier_order()
    {
        Assert.True(ProfileKeys.Same("Ctrl+Shift+1", "shift + ctrl+1"));
        Assert.True(ProfileKeys.Same("Control+K", "Ctrl+K"));
        Assert.False(ProfileKeys.Same("Ctrl+1", "1"));
        Assert.False(ProfileKeys.Same(null, null));
        Assert.False(ProfileKeys.Same("None", "None"));
        Assert.True(ProfileKeys.IsTaken("f10", ["F8", "F9", "F10", "F11"]));
        Assert.False(ProfileKeys.IsTaken("F12", ["F8", "F9", "F10", "F11"]));
    }

    [Fact]
    public void Next_profile_default_is_ctrl_F10_so_bare_F10_reaches_other_loaders()
    {
        // Kiln keeps a bound key from the game and the other loaders; XHL-Core opens its window on bare F10
        Assert.True(Kiln.KeyChord.TryParse(ProfileKeys.DefaultNextProfileKey, out var chord));
        Assert.Equal(new Kiln.KeyChord(0x79, Kiln.KeyMods.Ctrl), chord);
        string[] defaults = [KeyNames.DefaultMenuKey, KeyNames.DefaultAutoLootKey, ProfileKeys.DefaultNextProfileKey, ProfileKeys.DefaultFocusKey];
        foreach (var key in defaults) Assert.Single(defaults, k => ProfileKeys.Same(k, key));
        Assert.False(ProfileKeys.IsTaken("F10", defaults));
    }

    [Fact]
    public void Binding_ids_follow_safe_profile_ids()
    {
        Assert.Equal("kiln-auto-loot.profile.custom-1", ProfileKeys.BindingId("custom-1"));
        Assert.Equal("my-wood2", ProfileKeys.SafeId(" My-Wood2! "));
        Assert.Null(ProfileKeys.SafeId("!!!"));
        Assert.Null(ProfileKeys.SafeId(null));
        Assert.Equal(32, ProfileKeys.SafeId(new string('a', 50))!.Length);
        foreach (var id in BuiltInProfiles.Ids) Assert.Equal(id, ProfileKeys.SafeId(id));
    }

    [Fact]
    public void The_default_keys_are_names_Kiln_knows()
    {
        Assert.True(Kiln.KeyChord.TryParse(ProfileKeys.DefaultNextProfileKey, out _));
        Assert.True(Kiln.KeyChord.TryParse(ProfileKeys.DefaultFocusKey, out _));
        Assert.True(Kiln.KeyChord.TryParse(ProfileKeys.Normalize("ctrl + 1"), out _));
        Assert.False(Kiln.KeyChord.TryParse("NoSuchKey", out _));
    }
}
