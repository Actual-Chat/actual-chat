using System.Buffers.Binary;
using System.Text;

namespace ActualChat.Spans;

public ref struct SpanReader
{
    private const int SizeOfGuid = 16;

    private readonly ReadOnlySpan<byte> _span;
    private readonly int[] _decimalBits;

    public readonly int Length;
    public int Position;

    public SpanReader(ReadOnlySpan<byte> span)
    {
        Length = span.Length;
        Position = 0;
        _span = span;

        _decimalBits = new int[4];
    }

    public ReadOnlySpan<byte> Tail => _span[Position..];

    public ReadOnlySpan<byte> Span => _span;

    public ReadOnlySpan<byte> ReadSpan(int length, out bool success)
    {
        if (Position + length > _span.Length) {
            success = false;
            return ReadOnlySpan<byte>.Empty;
        }
        var span = _span[Position..(Position + length)];

        Position += length;

        success = true;
        return span;
    }

    public bool? ReadBoolean()
    {
        var result = ReadByte();
        return result.HasValue ? result != 0 : null;
    }

    public byte? ReadByte()
    {
        if (Position >= _span.Length)
            return null;

        var result = _span[Position];

        Position += sizeof(byte);

        return result;
    }

    public sbyte? ReadSByte() => (sbyte?)ReadByte();

    public short? ReadShort()
    {
        const int shortSize = sizeof(short);
        if (shortSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + shortSize)];
        var result = BinaryPrimitives.ReadInt16BigEndian(numSpan);
        Position += shortSize;

        return result;
    }

    public short? ReadInt16() => ReadShort();

    public int? ReadInt() => ReadInt32();

    public ushort? ReadUShort()
    {
        const int ushortSize = sizeof(ushort);
        if (ushortSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + ushortSize)];
        var result = BinaryPrimitives.ReadUInt16BigEndian(numSpan);
        Position += ushortSize;

        return result;
    }

    public uint? ReadUInt() => ReadUInt32();

    public uint? ReadUInt32()
    {
        const int uintSize = sizeof(uint);
        if (uintSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + uintSize)];
        var result = BinaryPrimitives.ReadUInt32BigEndian(numSpan);
        Position += uintSize;

        return result;
    }

    public int? ReadInt32()
    {
        const int intSize = sizeof(int);
        if (intSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + intSize)];
        var result = BinaryPrimitives.ReadInt32BigEndian(numSpan);
        Position += intSize;

        return result;
    }

    public long? ReadLong()
    {
        const int longSize = sizeof(long);
        if (longSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + longSize)];
        var result = BinaryPrimitives.ReadInt64BigEndian(numSpan);
        Position += longSize;

        return result;
    }

    public ulong? ReadULong()
    {
        const int ulongSize = sizeof(ulong);
        if (ulongSize + Position > _span.Length)
            return null;

        var numSpan = _span[Position..(Position + ulongSize)];
        var result = BinaryPrimitives.ReadUInt64BigEndian(numSpan);
        Position += ulongSize;

        return result;
    }

    public float? ReadFloat(int size)
    {
        var num = ReadULong(size);
        if (!num.HasValue)
            return null;

        switch (size) {
        case 4:
            return new NumericUnion { UInt = (uint)num.Value }.Float;
        case 8:
            return (float?)new NumericUnion { ULong = num.Value }.Double;
        default:
            throw StandardError.Format("Incorrect float length.");
        }
    }

    public long? ReadLong(int size)
    {
        if (Position + size > _span.Length)
            return null;

        long result = _span[Position];
        for (var i = 1; i < size; i++)
            result = (result << 8) | (uint)(_span[Position + i] & 0xFF);

        Position += size;

        return result;
    }

    public ulong? ReadULong(int size)
    {
        if (Position + size > _span.Length)
            return null;

        ulong result = 0;
        for (var i = 0; i < size; i++)
            result = (result << 8) | _span[Position + i];

        Position += size;

        return result;
    }

    public string? ReadAsciiString(int size) => ReadString(Encoding.ASCII, size);

    public string? ReadUtf8String(int size) => ReadString(Encoding.UTF8, size);

    public decimal? ReadDecimal()
    {
        var length = sizeof(decimal);
        if (Position + length > _span.Length)
            return null;

        var buffer = _span.Slice(Position, length);

        _decimalBits[0] = buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
        _decimalBits[1] = buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24);
        _decimalBits[2] = buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24);
        _decimalBits[3] = buffer[12] | (buffer[13] << 8) | (buffer[14] << 16) | (buffer[15] << 24);

        Position += length;

        return new decimal(_decimalBits);
    }

    public float? ReadSingle() => ReadFloat();

    public float? ReadFloat() => Read<float>();

    public double? ReadDouble() => Read<double>();

    public byte[]? ReadBytes(int length)
    {
        if (Position + length > _span.Length)
            return null;

        var result = _span.Slice(Position, length).ToArray();
        Position += length;
        return result;
    }

    public int ReadBytes(Span<byte> span, int length) => ReadBytes(span, 0, length);

    public int ReadBytes(Span<byte> span, int start, int length)
    {
        if (Position + start + length > _span.Length)
            return -1;

        _span.Slice(Position + start, length).CopyTo(span);
        Position += length;
        return length;
    }

    public DateTime? ReadDateTime()
    {
        var utcNowAsLong = ReadLong();
        if (!utcNowAsLong.HasValue)
            return null;

        return DateTime.FromBinary(utcNowAsLong.Value);
    }

    public Guid? ReadGuid()
    {
        var bytes = ReadBytes(SizeOfGuid);
        if (bytes is null)
            return null;

        return new Guid(bytes);
    }

    public T? Read<T>() where T : unmanaged
    {
        var sizeOf = Unsafe.SizeOf<T>();
        if (sizeOf + Position > _span.Length)
            return null;

        var newSpan = _span[Position..];
        var result = MemoryMarshal.Read<T>(newSpan);
        Position += sizeOf;

        return result;
    }

    // Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs#L590
    public int? Read7BitEncodedInt()
    {
        // Unlike writing, we can't delegate to the 64-bit read on
        // 64-bit platforms. The reason for this is that we want to
        // stop consuming bytes if we encounter an integer overflow.

        uint result = 0;
        byte? byteReadJustNow;

        // Read the integer 7 bits at a time. The high bit
        // of the byte when on means to continue reading more bytes.
        //
        // There are two failure cases: we've read more than 5 bytes,
        // or the fifth byte is about to cause integer overflow.
        // This means that we can read the first 4 bytes without
        // worrying about integer overflow.

        const int maxBytesWithoutOverflow = 4;

        for (var shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7) {
            // ReadByte handles end of stream cases for us.
            byteReadJustNow = ReadByte();
            if (!byteReadJustNow.HasValue)
                return null;

            result |= (byteReadJustNow.Value & 0x7Fu) << shift;

            if (byteReadJustNow <= 0x7Fu)
                return (int)result; // early exit
        }

        // Read the 5th byte. Since we already read 28 bits,
        // the value of this byte must fit within 4 bits (32 - 28),
        // and it must not have the high bit set.

        byteReadJustNow = ReadByte();
        if (!byteReadJustNow.HasValue)
            return null;

        if (byteReadJustNow > 0b_1111u)
            throw StandardError.Format("Too many bytes in what should have been a 7-bit encoded integer.");

        result |= (uint)byteReadJustNow << (maxBytesWithoutOverflow * 7);
        return (int)result;
    }

    public long? Read7BitEncodedInt64()
    {
        ulong result = 0;
        byte? byteReadJustNow;

        // Read the integer 7 bits at a time. The high bit
        // of the byte when on means to continue reading more bytes.
        //
        // There are two failure cases: we've read more than 10 bytes,
        // or the tenth byte is about to cause integer overflow.
        // This means that we can read the first 9 bytes without
        // worrying about integer overflow.

        const int MaxBytesWithoutOverflow = 9;
        for (var shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7) {
            // ReadByte handles end of stream cases for us.
            byteReadJustNow = ReadByte();
            if (!byteReadJustNow.HasValue)
                return null;

            result |= (byteReadJustNow.Value & 0x7Ful) << shift;

            if (byteReadJustNow <= 0x7Fu)
                return (long)result; // early exit
        }

        // Read the 10th byte. Since we already read 63 bits,
        // the value of this byte must fit within 1 bit (64 - 63),
        // and it must not have the high bit set.

        byteReadJustNow = ReadByte();
        if (!byteReadJustNow.HasValue)
            return null;

        if (byteReadJustNow > 0b_1u)
            throw StandardError.Format("Too many bytes in what should have been a 7-bit encoded integer.");

        result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
        return (long)result;
    }

    public VInt? ReadVInt(int maxLength = 4)
    {
        uint? b1 = ReadByte();
        if (!b1.HasValue)
            return null;

        ulong raw = b1.Value;
        uint mask = 0xFF00;

        for (var i = 0; i < maxLength; ++i) {
            mask >>= 1;

            if ((b1 & mask) == 0) continue;

            var value = raw & ~mask;

            for (var j = 0; j < i; ++j) {
                var b = ReadByte();
                if (!b.HasValue)
                    return null;

                raw = (raw << 8) | b.Value;
                value = (value << 8) | b.Value;
            }

            return VInt.EncodeSize(raw, i + 1);
        }

        return null;
    }

    private string? ReadString(Encoding decoder, int size)
    {
        if (size == 0)
            return "";

        if (Position + size > _span.Length)
            return null;

        var result = decoder.GetString(_span.Slice(Position, size));

        Position += size;

        return result;
    }
}
