// SPDX-License-Identifier: MIT
// Salvage's two messages on the wire, and the channel version they moved.
using Kiln;
using KilnAutoLoot.Core;
using KilnAutoLoot.Net;
using Xunit;

namespace KilnAutoLoot.Offline.Wire;

public sealed class SalvageMessageTests
{
    private static byte[] Bytes(Action<NetWriter> write)
    {
        var w = new NetWriter();
        write(w);
        return w.ToArray();
    }

    [Fact]
    public void A_command_round_trips_with_its_tokens()
    {
        var m = new SalvageCmdMsg(SalvageCmd.Salvage, [1UL << 40 | 7, 99]);
        var r = new NetReader(Bytes(m.Encode));
        var back = SalvageCmdMsg.Decode(ref r);
        Assert.True(r.End);
        Assert.Equal(SalvageCmd.Salvage, back.Kind);
        Assert.Equal(new[] { 1UL << 40 | 7, 99UL }, back.Tokens);
    }

    [Theory]
    [InlineData((byte)0)] [InlineData((byte)200)]
    public void An_unknown_command_is_refused(byte kind)
    {
        var bytes = Bytes(w => w.Byte(kind).Int32(0));
        Assert.Throws<NetFormatException>(() =>
        {
            var r = new NetReader(bytes);
            SalvageCmdMsg.Decode(ref r);
        });
    }

    [Theory]
    [InlineData(SalvageCmdMsg.MaxTokens + 1)] [InlineData(-1)]
    public void A_token_count_out_of_bounds_is_refused(int count)
    {
        var bytes = Bytes(w => w.Byte((byte)SalvageCmd.Mark).Int32(count));
        Assert.Throws<NetFormatException>(() =>
        {
            var r = new NetReader(bytes);
            SalvageCmdMsg.Decode(ref r);
        });
    }

    [Fact]
    public void The_state_round_trips_and_the_most_rows_fit_one_message()
    {
        var rows = Enumerable.Range(0, SalvageStateMsg.MaxRows + 3)
            .Select(i => new SalvageRow((ulong)i, 0x43108313u, 34, 4, SalvageRowFlags.Marked | SalvageRowFlags.InChest,
                SalvageBlock.Gem, 1210))
            .ToArray();
        var m = new SalvageStateMsg(SalvageReady.Ready, 0x41a1u, 12, rows, new SalvageOutcome(3, 60, 1, SalvageBlock.Changed), Busy: true);
        var bytes = Bytes(m.Encode);
        Assert.True(bytes.Length <= Channels.MaxMessageBytes);
        var r = new NetReader(bytes);
        var back = SalvageStateMsg.Decode(ref r);
        Assert.True(r.End);
        Assert.Equal(SalvageStateMsg.MaxRows, back.Rows.Length);
        Assert.Equal(rows[5], back.Rows[5]);
        Assert.Equal(12, back.OwedRunes);
        Assert.Equal(0x41a1u, back.RuneItem);
        Assert.Equal(new SalvageOutcome(3, 60, 1, SalvageBlock.Changed), back.Last);
        Assert.True(back.Busy);
    }

    [Fact]
    public void The_channel_version_moved_with_the_salvage_messages() => Assert.Equal("3", Channels.Version);
}
