// SPDX-License-Identifier: MIT
// The game's texts of one language, read from its text blob: a list of 24-byte records { u32 tag id; text (s32 offset
// from the text field, u32 length in bytes); the text's arguments }, sorted by tag id. A text is looked up by its tag
// id, the FNV-1a-32 of the text reference's 16 GUID bytes. Pure: no Kiln calls.
using System.Buffers.Binary;
using System.Text;

namespace KilnAutoLoot.Core;

public static class LocaBlob
{
    public const int RecordSize = 24;

    /// <summary>Every text of the blob by tag id. A blob that does not read as such a list gives what was read up to the
    /// first broken record (an empty table for a blob of another shape).</summary>
    public static Dictionary<uint, string> Parse(ReadOnlySpan<byte> blob, int recordSize = RecordSize, int idOffset = 0, int textOffset = 4)
    {
        var table = new Dictionary<uint, string>();
        if (blob.Length < 8 || recordSize < 12) return table;
        long start = BinaryPrimitives.ReadInt32LittleEndian(blob);
        long count = BinaryPrimitives.ReadUInt32LittleEndian(blob[4..]);
        if (start < 8 || count > 4_000_000 || start + count * recordSize > blob.Length) return table;
        for (long i = 0; i < count; i++)
        {
            var at = start + i * recordSize;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(blob[(int)(at + idOffset)..]);
            var field = at + textOffset;
            long rel = BinaryPrimitives.ReadInt32LittleEndian(blob[(int)field..]);
            long len = BinaryPrimitives.ReadUInt32LittleEndian(blob[(int)(field + 4)..]);
            if (len == 0) continue;
            var text = field + rel;
            if (text < 0 || len > 1 << 16 || text + len > blob.Length) break;
            table.TryAdd(id, Encoding.UTF8.GetString(blob.Slice((int)text, (int)len)));
        }
        return table;
    }

    /// <summary>The tag id of a text reference: FNV-1a-32 of the GUID's 16 bytes in .NET's order.</summary>
    public static uint TagOf(Guid reference)
    {
        Span<byte> b = stackalloc byte[16];
        reference.TryWriteBytes(b);
        var h = 0x811c9dc5u;
        foreach (var x in b) h = (h ^ x) * 0x01000193u;
        return h;
    }

    /// <summary>A text without the game's markup tags (&lt;b&gt;, &lt;lorename&gt;, ...), trimmed.</summary>
    public static string Plain(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '<')
            {
                var close = text.IndexOf('>', i + 1);
                if (close > i && close - i <= 65 && text.IndexOf('<', i + 1, close - i - 1) < 0)
                {
                    i = close;
                    continue;
                }
            }
            sb.Append(text[i]);
        }
        return sb.ToString().Trim();
    }
}
