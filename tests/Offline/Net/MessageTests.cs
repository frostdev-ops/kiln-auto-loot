// SPDX-License-Identifier: MIT
// The wire form of the mod's messages: the skip ids in PersonalMsg (bounded on decode) and Enabled in ServerInfoMsg.
using Kiln;
using KilnAutoLoot.Net;
using Xunit;

namespace KilnAutoLoot.Offline.Wire;

public sealed class MessageTests
{
    private static byte[] Bytes(Action<NetWriter> write)
    {
        var w = new NetWriter();
        write(w);
        return w.ToArray();
    }

    [Fact]
    public void PersonalMsg_round_trips_the_json_and_the_skip_ids()
    {
        var m = new PersonalMsg("{\"skipList\":\"=Apple\"}", [6, 1, 4_000_000_000]);
        var bytes = Bytes(m.Encode);
        var r = new NetReader(bytes);
        var back = PersonalMsg.Decode(ref r);
        Assert.True(r.End);
        Assert.Equal(m.Json, back.Json);
        Assert.Equal(m.SkipIds, back.SkipIds);

        var empty = new PersonalMsg("{}", []);
        var r2 = new NetReader(Bytes(empty.Encode));
        Assert.Empty(PersonalMsg.Decode(ref r2).SkipIds);
    }

    [Fact]
    public void PersonalMsg_encodes_at_most_the_bound_of_ids()
    {
        var m = new PersonalMsg("{}", Enumerable.Range(0, Channels.MaxSkipIds + 10).Select(i => (uint)i).ToArray());
        var bytes = Bytes(m.Encode);
        Assert.True(bytes.Length <= Channels.MaxMessageBytes);
        var r = new NetReader(bytes);
        Assert.Equal(Channels.MaxSkipIds, PersonalMsg.Decode(ref r).SkipIds.Length);
    }

    [Theory]
    [InlineData(Channels.MaxSkipIds + 1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void PersonalMsg_decode_refuses_a_count_out_of_bounds(int count)
    {
        var bytes = Bytes(w => w.String("{}").Int32(count));
        Assert.Throws<NetFormatException>(() =>
        {
            var r = new NetReader(bytes);
            PersonalMsg.Decode(ref r);
        });
    }

    [Fact]
    public void PersonalMsg_decode_of_a_count_longer_than_the_data_fails()
    {
        var bytes = Bytes(w => w.String("{}").Int32(3).UInt32(1));
        Assert.ThrowsAny<Exception>(() =>
        {
            var r = new NetReader(bytes);
            PersonalMsg.Decode(ref r);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServerInfoMsg_round_trips_Enabled(bool enabled)
    {
        var m = new ServerInfoMsg("{\"radius\":5}", "{\"maxRadius\":20}", 500, true, false, enabled);
        var r = new NetReader(Bytes(m.Encode));
        var back = ServerInfoMsg.Decode(ref r);
        Assert.True(r.End);
        Assert.Equal(m, back);
        Assert.Equal(enabled, back.Enabled);
    }
}
