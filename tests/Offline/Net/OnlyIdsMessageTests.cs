// SPDX-License-Identifier: MIT
// PersonalMsg's only-list ids and the ServerInfoMsg fields of the rules wave on the wire.
using Kiln;
using KilnAutoLoot.Net;
using Xunit;

namespace KilnAutoLoot.Offline.Wire;

public sealed class OnlyIdsMessageTests
{
    private static byte[] Bytes(Action<NetWriter> write)
    {
        var w = new NetWriter();
        write(w);
        return w.ToArray();
    }

    [Fact]
    public void PersonalMsg_round_trips_the_only_ids_after_the_skip_ids()
    {
        var m = new PersonalMsg("{}", [6, 2], [1, 4_000_000_000]);
        var r = new NetReader(Bytes(m.Encode));
        var back = PersonalMsg.Decode(ref r);
        Assert.True(r.End);
        Assert.Equal(new uint[] { 6, 2 }, back.SkipIds);
        Assert.Equal(new uint[] { 1, 4_000_000_000 }, back.OnlyIds);
    }

    [Fact]
    public void The_two_argument_PersonalMsg_sends_no_only_ids()
    {
        var r = new NetReader(Bytes(new PersonalMsg("{}", [6]).Encode));
        var back = PersonalMsg.Decode(ref r);
        Assert.Empty(back.OnlyIds);
        Assert.Equal(new uint[] { 6 }, back.SkipIds);
    }

    [Fact]
    public void Both_lists_at_their_bound_fit_the_largest_message()
    {
        var many = Enumerable.Range(0, Channels.MaxSkipIds + 5).Select(i => (uint)i).ToArray();
        var m = new PersonalMsg(new string('x', 1000), many, many);
        var bytes = Bytes(m.Encode);
        Assert.True(bytes.Length <= Channels.MaxMessageBytes);
        var r = new NetReader(bytes);
        var back = PersonalMsg.Decode(ref r);
        Assert.Equal(Channels.MaxSkipIds, back.SkipIds.Length);
        Assert.Equal(Channels.MaxSkipIds, back.OnlyIds.Length);
        Assert.True(Channels.MaxMessageBytes < 256 * 1024);
    }

    [Theory]
    [InlineData(Channels.MaxSkipIds + 1)]
    [InlineData(-1)]
    public void An_only_id_count_out_of_bounds_is_refused(int count)
    {
        var bytes = Bytes(w => w.String("{}").Int32(0).Int32(count));
        Assert.Throws<NetFormatException>(() =>
        {
            var r = new NetReader(bytes);
            PersonalMsg.Decode(ref r);
        });
    }

    [Fact]
    public void The_channel_version_changed_with_the_wire_layout()
    {
        // test build 1 sent JSON and skip ids only, on channel "1": this decoder cannot read that, so the version moved
        var old = Bytes(w =>
        {
            w.String("{}").Int32(1);
            w.UInt32(6);
        });
        Assert.ThrowsAny<Exception>(() =>
        {
            var r = new NetReader(old);
            PersonalMsg.Decode(ref r);
        });
        Assert.Equal("2", Channels.Version);
    }

    [Fact]
    public void All_three_lists_at_their_bounds_fit_one_message()
    {
        string List(string prefix, string suffix) => string.Join(",", Enumerable.Range(0, KilnAutoLoot.Core.SkipListBounds.MaxEntries)
            .Select(i => prefix + i.ToString("D3") + new string('x', KilnAutoLoot.Core.SkipListBounds.MaxEntryLength - prefix.Length - 3 - suffix.Length) + suffix));
        var s = new KilnAutoLoot.Core.PersonalSettings
        {
            SkipList = List("=", ""), OnlyList = List("#", ""), CarryLimits = List("=", ":9999999"),
        };
        Assert.True(KilnAutoLoot.Core.SettingsRules.ListsWithin(s));
        Assert.True(KilnAutoLoot.Core.SettingsRules.FitsMessage(s));
        var json = SettingsJson.Write(s);
        var many = Enumerable.Range(0, Channels.MaxSkipIds).Select(i => (uint)i).ToArray();
        var bytes = Bytes(new PersonalMsg(json, many, many).Encode);
        Assert.True(bytes.Length <= Channels.MaxMessageBytes);
        Assert.True(Channels.MaxMessageBytes < 256 * 1024);
        var r = new NetReader(bytes);
        Assert.Equal(json, PersonalMsg.Decode(ref r).Json);
    }

    [Fact]
    public void Settings_over_the_json_bound_do_not_fit_and_the_server_keeps_the_previous_ones()
    {
        // escaped characters make the JSON longer than the lists' own bounds allow for
        string List() => string.Join(",", Enumerable.Range(0, KilnAutoLoot.Core.SkipListBounds.MaxEntries)
            .Select(_ => "=" + new string('<', KilnAutoLoot.Core.SkipListBounds.MaxEntryLength - 1)));
        var s = new KilnAutoLoot.Core.PersonalSettings { SkipList = List(), OnlyList = List(), CarryLimits = List(), Radius = 7 };
        Assert.True(KilnAutoLoot.Core.SettingsRules.ListsWithin(s));
        Assert.False(KilnAutoLoot.Core.SettingsRules.FitsMessage(s));

        var rig = new KilnAutoLoot.Offline.LootServerTests.LoopRig(x => x.Radius = 12);
        rig.Loop.SetPersonal(rig.Player, s);
        Assert.Equal(12f, rig.Loop.SettingsOf(rig.Player).Radius);
        Assert.Single(rig.KeyLines, l => l.Contains("bytes as JSON"));
    }

    [Fact]
    public void ServerInfoMsg_carries_line_of_sight_and_the_focus_item()
    {
        var m = new ServerInfoMsg("{}", "{}", 9999, false, true, true, LineOfSight: true, FocusItem: 77);
        var r = new NetReader(Bytes(m.Encode));
        var back = ServerInfoMsg.Decode(ref r);
        Assert.True(back.LineOfSight);
        Assert.Equal(77u, back.FocusItem);
    }
}
