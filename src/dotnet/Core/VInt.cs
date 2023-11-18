/* Copyright (c) 2011-2020 Oleg Zee

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

namespace ActualChat;

/// <summary>
///     Variable size integer implementation as of http://www.matroska.org/technical/specs/rfc/index.html
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct VInt : IEquatable<VInt>
{
    private const ulong MaxValue = (1L << 56) - 1;
    private const ulong UnknownSizeValue = MaxValue | (1ul << 56);
    private const ulong MaxSizeValue = MaxValue - 1;
    // ReSharper disable once UnusedMember.Local
    private const ulong MaxElementIdValue = (1 << 28) - 1;

    private static readonly sbyte[] ExtraBytesSize =
        { 4, 3, 2, 2, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static readonly ulong[] DataBitsMask = {
        (1L << 0) - 1,
        (1L << 7) - 1,
        (1L << 14) - 1,
        (1L << 21) - 1,
        (1L << 28) - 1,
        (1L << 35) - 1,
        (1L << 42) - 1,
        (1L << 49) - 1,
        (1L << 56) - 1,
    };

    private readonly byte _length;

    private VInt(ulong encodedValue, int length)
    {
        if (length < 1 || (length > 8 && encodedValue != UnknownSizeValue))
            throw new ArgumentOutOfRangeException(nameof(length));

        EncodedValue = encodedValue;
        _length = (byte)length;
    }

    public ulong EncodedValue { get; }

    public ulong Value => EncodedValue & DataBitsMask[_length];

    public bool IsReserved => Value == DataBitsMask[_length];

    public bool IsValidIdentifier {
        get {
            var isShortest = _length == 1 || Value > DataBitsMask[_length - 1];
            return isShortest && !IsReserved;
        }
    }

    public uint Length => _length;

    public static implicit operator ulong?(VInt value)
        => !value.IsReserved ? value.Value : null;

    public static readonly VInt Unknown = UnknownSize(2);

    public static VInt EncodeSize(ulong value, int length = 0)
    {
        if (length < 0 || length > 8)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (value == MaxValue)
            return new VInt(UnknownSizeValue, 8);

        if (value == UnknownSizeValue)
            return new VInt(value, 8);

        var marker = 1UL << (7 * length);

        if ((value & (marker - 1)) > MaxSizeValue)
            throw new ArgumentException("Value exceed VInt capacity", nameof(value));

        if (length == 0) {
            while (DataBitsMask[++length] <= value) { }
            marker = 1UL << (7 * length);
        }

        if (length > 0 && (DataBitsMask[length] | marker) <= value)
            throw new ArgumentException("Specified width is not sufficient to encode value", nameof(value));

        return new VInt(value | marker, length);
    }

    public static VInt UnknownSize(int length)
    {
        if (length < 0 || length > 8)
            throw new ArgumentOutOfRangeException(nameof(length));

        var sizeMarker = 1UL << (7 * length);
        var dataBits = (1UL << (7 * length)) - 1;
        return new VInt(sizeMarker | dataBits, length);
    }

    public static VInt FromEncoded(ulong encodedValue)
    {
        if (encodedValue == 0)
            throw new ArgumentException("Zero is not a correct value", nameof(encodedValue));

        var mostSignificantOctetIndex = 7;
        while (encodedValue >> (mostSignificantOctetIndex * 8) == 0x0)
            mostSignificantOctetIndex--;

        var marker = (byte)((encodedValue >> (mostSignificantOctetIndex * 8)) & 0xff);
        var extraBytes = marker >> 4 > 0 ? ExtraBytesSize[marker >> 4] : 4 + ExtraBytesSize[marker];

        if (extraBytes != mostSignificantOctetIndex)
            throw new ArgumentException("Width marker does not match its position", nameof(encodedValue));

        return new VInt(encodedValue, extraBytes + 1);
    }

    public static VInt FromValue(ulong value)
        => new (value, (int)GetSize(value));

    public static VInt FromValue(long value)
    {
        var size = (int)GetSize(value);
        return value > 0 ? new VInt((ulong)value, size) : new VInt((ulong)-value | (1UL << ((8 * size) - 1)), size);
    }

    public static ulong GetSize(ulong value)
    {
        ulong length = 1;
        if ((value & 0xFFFFFFFF00000000) != 0) {
            length += 4;
            value >>= 32;
        }
        if ((value & 0xFFFF0000) != 0) {
            length += 2;
            value >>= 16;
        }
        if ((value & 0xFF00) != 0) length++;
        return length;
    }

    public static ulong GetSize(long value)
    {
        var v = (ulong)value;
        if (value < 0)
            v = ~v;
        ulong length = 1;
        if ((v & 0xFFFFFFFF00000000) != 0) {
            length += 4;
            v >>= 32;
        }
        if ((v & 0xFFFF0000) != 0) {
            length += 2;
            v >>= 16;
        }
        if ((v & 0xFF00) != 0) {
            length += 1;
            v >>= 8;
        }
        // We have at most 8 bits left.
        // Is the most significant bit set (or cleared for a negative number),
        // then we need an extra byte for the sign bit.
        if ((v & 0x80) != 0)
            length++;

        return length;
    }

    // Equality

    public override int GetHashCode()
    {
        unchecked {
            return (EncodedValue.GetHashCode() * 397) ^ _length.GetHashCode();
        }
    }

    public bool Equals(VInt other)
        => other.EncodedValue == EncodedValue && other._length == _length;
    public override bool Equals(object? obj)
        => obj is VInt i && Equals(i);
    public static bool operator ==(VInt left, VInt right) => left.Equals(right);
    public static bool operator !=(VInt left, VInt right) => !left.Equals(right);

    // ToString

    public override string ToString() => $"VInt({EncodedValue:X})";
}
