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
    public void ServerInfoMsg_carries_line_of_sight_and_the_focus_item()
    {
        var m = new ServerInfoMsg("{}", "{}", 9999, false, true, true, LineOfSight: true, FocusItem: 77);
        var r = new NetReader(Bytes(m.Encode));
        var back = ServerInfoMsg.Decode(ref r);
        Assert.True(back.LineOfSight);
        Assert.Equal(77u, back.FocusItem);
    }
}
