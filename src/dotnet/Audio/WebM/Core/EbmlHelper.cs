using System.Text;
using ActualChat.Audio.WebM.Models;
using ActualChat.Spans;

namespace ActualChat.Audio.WebM;

public static class EbmlHelper
{
    private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;

    public static ulong GetSize(ulong value)
        => VInt.GetSize(value);

    public static ulong GetSize(long value)
        => VInt.GetSize(value);

    public static ulong GetSize(double value) => 8;

    public static ulong GetSize(float value) => 4;

    public static ulong GetSize(DateTime value) => 8;

    public static ulong GetSize(string value, bool isAscii)
    {
        var encoding = isAscii
            ? Encoding.ASCII
            : Encoding.UTF8;
        return (ulong)encoding.GetByteCount(value);
    }

    public static ulong GetCodedSize(ulong value)
    {
        if (value < 0x000000000000007F)
            return 1;
        if (value < 0x0000000000003FFF)
            return 2;
        if (value < 0x00000000001FFFFF)
            return 3;
        if (value < 0x000000000FFFFFFF)
            return 4;
        if (value < 0x00000007FFFFFFFF)
            return 5;
        if (value < 0x000003FFFFFFFFFF)
            return 6;
        if (value < 0x0001FFFFFFFFFFFF)
            return 7;

        return 8;
    }

    public static ulong GetElementSize(ulong identifier, ulong value)
        => GetSize(identifier) + GetSize(value) + 1;

    public static ulong GetElementSize(ulong identifier, ulong? value)
        => value.HasValue ? GetElementSize(identifier, value.Value) : 0;

    public static ulong GetElementSize(ulong identifier, long? value)
        => value.HasValue ? GetElementSize(identifier, value.Value) : 0;

    public static ulong GetElementSize(ulong identifier, long value)
        => GetSize(identifier) + GetSize(value) + 1;

    public static ulong GetElementSize(ulong identifier, double value)
        => GetSize(identifier) + GetSize(value) + 1;

    public static ulong GetElementSize(ulong identifier, double? value)
        => value.HasValue ? GetElementSize(identifier, value.Value) : 0;

    public static ulong GetElementSize(ulong identifier, float value)
        => GetSize(identifier) + GetSize(value) + 1;

    public static ulong GetElementSize(ulong identifier, float? value)
        => value.HasValue ? GetElementSize(identifier, value.Value) : 0;

    public static ulong GetElementSize(ulong identifier, DateTime value)
        => GetSize(identifier) + GetSize(value) + 1;

    public static ulong GetElementSize(ulong identifier, DateTime? value)
        => value.HasValue ? GetElementSize(identifier, value.Value) : 0;

    public static ulong GetElementSize(ulong identifier, byte[]? value)
        => value == null ? 0UL : GetSize(identifier) + (ulong)value.Length + 1;

    public static ulong GetElementSize(ulong identifier, string? value, bool isAscii)
        => value == null ? 0UL : GetSize(identifier) + GetSize(value, isAscii) + 1;

    public static ulong GetMasterElementSize(ulong identifier, ulong size)
        => GetSize(identifier) + GetCodedSize(size);

    public static ulong GetElementSize(ulong identifier, BaseModel? value)
        => value == null ? 0 : GetSize(identifier) + value.GetSize() + 1;

    public static ulong GetElementSize(ulong identifier, IReadOnlyList<BaseModel>? value)
        => value?.Aggregate(0UL, (size, m) => size + GetElementSize(identifier, m)) ?? 0UL;

    public static ulong GetElementSize(ulong identifier, IReadOnlyList<Block>? value)
        => value?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;

    public static ulong GetElementSize(ulong identifier, IReadOnlyList<SimpleTag>? value)
        => value?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;

    public static ulong GetSize(this IReadOnlyList<BaseModel>? list)
        => list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;

    public static ulong GetSize(this IReadOnlyList<Block>? list)
        => list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;

    public static ulong GetSize(this IReadOnlyList<SimpleTag>? list)
        => list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;

    public static bool WriteEbmlMasterElement(ulong identifier, ulong size, ref SpanWriter writer)
    {
        if (size != UnknownSize) {
            var totalSize = GetMasterElementSize(identifier, size) + size;
            if (writer.Position + (int)totalSize > writer.Length)
                return false;
        }

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(size));

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, ulong value, ref SpanWriter writer)
    {
        var totalSize = GetElementSize(identifier, value);
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(GetSize(value)));
        writer.Write(VInt.FromValue(value));

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, ulong? value, ref SpanWriter writer)
        => value == null || WriteEbmlElement(identifier, value.Value, ref writer);

    public static bool WriteEbmlElement(ulong identifier, long value, ref SpanWriter writer)
    {
        var totalSize = GetElementSize(identifier, value);
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(GetSize(value)));
        writer.Write(VInt.FromValue(value));

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, long? value, ref SpanWriter writer)
        => value == null || WriteEbmlElement(identifier, value.Value, ref writer);

    public static bool WriteEbmlElement(ulong identifier, DateTime value, ref SpanWriter writer)
    {
        var totalSize = GetElementSize(identifier, value);
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        return WriteEbmlElement(identifier, value.Ticks, ref writer);
    }

    public static bool WriteEbmlElement(ulong identifier, DateTime? value, ref SpanWriter writer)
        => value == null || WriteEbmlElement(identifier, value.Value, ref writer);

    public static bool WriteEbmlElement(ulong identifier, double value, ref SpanWriter writer)
    {
        var totalSize = GetElementSize(identifier, value);
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(GetSize(value)));
        writer.Write(value);

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, double? value, ref SpanWriter writer)
        => value == null || WriteEbmlElement(identifier, value.Value, ref writer);

    public static bool WriteEbmlElement(ulong identifier, float value, ref SpanWriter writer)
    {
        var totalSize = GetElementSize(identifier, value);
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        var size = GetSize(value);
        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(size));
        if (size == 4) {
            var uInt = new NumericUnion { Float = value }.UInt;
            for (var i = 1; i <= 4; i++) {
                var bytes = 4 - i;
                var bits = bytes * 8;
                var bElement = (byte)((uInt >> bits) & 0xFFU);
                writer.Write(bElement);
            }
        }
        else
            throw StandardError.NotSupported("Unable to write double (8-byte length).");

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, float? value, ref SpanWriter writer)
        => value == null || WriteEbmlElement(identifier, value.Value, ref writer);

    public static bool WriteEbmlElement(ulong identifier, string? value, bool isAscii, ref SpanWriter writer)
    {
        if (value == null)
            return true;

        var size = GetSize(value, isAscii);
        var totalSize = GetSize(identifier) + size + 1;
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(size));

        var encoding = isAscii ? Encoding.ASCII : Encoding.UTF8;
        writer.Write(value, encoding);

        return true;
    }

    public static bool WriteEbmlElement(ulong identifier, byte[]? value, ref SpanWriter writer)
    {
        if (value == null)
            return true;

        var size = (ulong)value.Length;
        var totalSize = GetSize(identifier) + size + 1;
        if (writer.Position + (int)totalSize > writer.Length)
            return false;

        writer.Write(VInt.FromEncoded(identifier));
        writer.Write(VInt.EncodeSize(size));
        writer.Write(value);

        return true;
    }

    public static bool Write(this IReadOnlyList<BaseModel>? models, ref SpanWriter writer)
    {
        if (models == null)
            return true;

        foreach (var model in models)
            // ReSharper disable once ConstantConditionalAccessQualifier
            if (!model?.Write(ref writer) ?? true)
                return false;

        return true;
    }

    public static bool Write(this IReadOnlyList<Block>? blocks, ref SpanWriter writer)
    {
        if (blocks == null)
            return true;

        foreach (var block in blocks)
            // ReSharper disable once ConstantConditionalAccessQualifier
            if (!block?.Write(ref writer) ?? true)
                return false;

        return true;
    }

    public static bool Write(this IReadOnlyList<SimpleTag>? tags, ref SpanWriter writer)
    {
        if (tags == null)
            return true;

        foreach (var tag in tags)
            // ReSharper disable once ConstantConditionalAccessQualifier
            if (!tag?.Write(ref writer) ?? true)
                return false;

        return true;
    }
}
