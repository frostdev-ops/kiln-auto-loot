// SPDX-License-Identifier: MIT
// Reads fields out of one resource's bytes with the game's reflected types (kiln.types), so a whole template is copied
// out of kiln.assets once and every field the rules need is read from that one copy. The blob kinds are self-relative:
//   BlobArray    { s32 offset to the first element, from the field; u32 count }
//   BlobString   { s32 offset to the text, from the field; u32 length in bytes }
//   BlobOptional { s32 offset to the value, from the field; 0 = not set }
//   BlobVariant  { u32 type hash (0 = empty); s32 offset to the value, from the second word }
// No native calls beyond GameTypes lookups; any thread.
using System.Buffers.Binary;
using System.Text;
using Kiln;

namespace KilnAutoLoot.Server.GameData;

/// <summary>A place in a resource's bytes: the type found there and its offset.</summary>
internal readonly record struct BlobSpot(GameType Type, int Offset);

internal sealed class BlobView(byte[] data)
{
    public byte[] Data { get; } = data;

    private int I32(long at) => at < 0 || at > Data.Length - 4 ? throw new InvalidDataException("outside the resource")
        : BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan((int)at));

    private uint U32(long at) => at < 0 || at > Data.Length - 4 ? throw new InvalidDataException("outside the resource")
        : BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan((int)at));

    /// <summary>The elements of a BlobArray at <paramref name="spot"/> (or a static array), as spots.</summary>
    public IEnumerable<BlobSpot> Elements(BlobSpot spot)
    {
        var t = spot.Type.Resolved;
        if (t.Inner is not { } inner) yield break;
        var e = inner.Resolved;
        if (t.Kind == GameTypeKind.StaticArray)
        {
            for (var i = 0; i < t.ArrayLength; i++) yield return new BlobSpot(e, spot.Offset + i * inner.Size);
            yield break;
        }
        if (t.Kind != GameTypeKind.BlobArray) yield break;
        var start = spot.Offset + (long)I32(spot.Offset);
        var count = U32(spot.Offset + 4L);
        if (count == 0 || inner.Size <= 0 || start < 0 || start + (long)count * inner.Size > Data.Length) yield break;
        for (var i = 0; i < count; i++) yield return new BlobSpot(e, (int)(start + (long)i * inner.Size));
    }

    /// <summary>The value an optional or a variant holds, or null when it is empty. Other spots come back unchanged.</summary>
    public BlobSpot? Enter(BlobSpot spot)
    {
        var t = spot.Type.Resolved;
        switch (t.Kind)
        {
            case GameTypeKind.BlobOptional when t.Inner is { } inner:
            {
                var rel = I32(spot.Offset);
                return rel == 0 ? null : new BlobSpot(inner.Resolved, spot.Offset + rel);
            }
            case GameTypeKind.BlobVariant:
            {
                var hash = U32(spot.Offset);
                if (hash == 0 || GameTypes.ByHash(hash) is not { } held) return null;
                return new BlobSpot(held.Resolved, spot.Offset + 4 + I32(spot.Offset + 4L));
            }
            default:
                return new BlobSpot(t, spot.Offset);
        }
    }

    /// <summary>The field <paramref name="path"/> below a struct spot ("a.b", "list[2].c"); optionals and variants on the way
    /// are entered. Null when the path leads nowhere in this resource.</summary>
    public BlobSpot? Find(BlobSpot from, string path)
    {
        BlobSpot? at = from;
        foreach (var raw in path.Split('.'))
        {
            var name = raw;
            int? index = null;
            var b = name.IndexOf('[');
            if (b > 0 && name.EndsWith(']') && int.TryParse(name[(b + 1)..^1], out var i))
            {
                index = i;
                name = name[..b];
            }
            if (at is not { } s || Enter(s) is not { } inside || inside.Type.Kind != GameTypeKind.Struct) return null;
            if (inside.Type.Field(name) is not { } f) return null;
            at = new BlobSpot(f.Type.Resolved, inside.Offset + f.Offset);
            if (index is { } k) at = Elements(at.Value).Skip(k).Cast<BlobSpot?>().FirstOrDefault();
        }
        return at;
    }

    /// <summary>The plain value at a spot: bool, long, ulong, double, string or Guid. A one-field struct reads as its field;
    /// an optional is entered. Null for anything else.</summary>
    public object? Value(BlobSpot? spot)
    {
        if (spot is not { } s) return null;
        for (var depth = 0; depth < 8; depth++)
        {
            var t = s.Type.Resolved;
            if (t.Kind == GameTypeKind.BlobOptional)
            {
                if (Enter(s) is not { } inner) return null;
                s = inner;
                continue;
            }
            if (t.Kind == GameTypeKind.Struct && t.AllFields.Take(2).ToArray() is [var only])
            {
                s = new BlobSpot(only.Type.Resolved, s.Offset + only.Offset);
                continue;
            }
            if (s.Offset < 0 || s.Offset + t.Size > Data.Length) return null;
            if (GameValueCodec.IsScalar(t.Kind))
            {
                var v = GameValueCodec.Read(Data.AsSpan(s.Offset, t.Size), t.Kind, t.Size, t.EnumValues);
                // enums read as their name, as the game's reflection spells it
                if (t.Kind == GameTypeKind.Enum && v is long n)
                    foreach (var (ename, evalue) in t.EnumValues) if (evalue == n) return ename;
                return v;
            }
            return t.Kind switch
            {
                GameTypeKind.BlobString => Text(s.Offset),
                GameTypeKind.Guid or GameTypeKind.ObjectReference when t.Size == 16 => new Guid(Data.AsSpan(s.Offset, 16)),
                _ => null,
            };
        }
        return null;
    }

    private string Text(int at)
    {
        var len = U32(at + 4L);
        if (len == 0) return "";
        var start = at + (long)I32(at);
        if (start < 0 || start + len > Data.Length) return "";
        return Encoding.UTF8.GetString(Data, (int)start, (int)len);
    }

    /// <summary>Every leaf below a spot as "path = value" lines (lists cut after <paramref name="maxArray"/>), for the probe and
    /// the debug dump.</summary>
    public void Describe(BlobSpot spot, string prefix, List<string> lines, int maxArray = 8, int maxLines = 400, int depth = 0)
    {
        if (lines.Count >= maxLines || depth > 12) return;
        var t = spot.Type.Resolved;
        try
        {
            switch (t.Kind)
            {
                case GameTypeKind.Struct:
                    foreach (var f in t.AllFields)
                        Describe(new BlobSpot(f.Type.Resolved, spot.Offset + f.Offset), prefix.Length == 0 ? f.Name : prefix + "." + f.Name,
                            lines, maxArray, maxLines, depth + 1);
                    break;
                case GameTypeKind.StaticArray or GameTypeKind.BlobArray:
                {
                    var i = 0;
                    if (t.Kind == GameTypeKind.BlobArray) lines.Add($"{prefix} = [{U32(spot.Offset + 4L)}]");
                    foreach (var e in Elements(spot))
                    {
                        if (i >= maxArray) break;
                        Describe(e, $"{prefix}[{i++}]", lines, maxArray, maxLines, depth + 1);
                    }
                    break;
                }
                case GameTypeKind.BlobOptional:
                    if (Enter(spot) is { } o) Describe(o, prefix, lines, maxArray, maxLines, depth + 1);
                    else lines.Add($"{prefix} = <not set>");
                    break;
                case GameTypeKind.BlobVariant:
                    if (Enter(spot) is { } v)
                    {
                        lines.Add($"{prefix} = <{v.Type.Name}>");
                        Describe(v, prefix, lines, maxArray, maxLines, depth + 1);
                    }
                    else lines.Add($"{prefix} = <empty>");
                    break;
                default:
                    lines.Add($"{prefix} = {Value(spot) ?? $"<{t.Kind}>"}");
                    break;
            }
        }
        catch (InvalidDataException)
        {
            lines.Add($"{prefix} = <outside the resource>");
        }
    }
}
