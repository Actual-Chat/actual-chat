using System.Buffers.Binary;
using System.Text;

namespace ActualChat.Spans;

public ref struct SpanWriter
{
    private const int BufferLength = 16;
    // private const int LargeByteBufferSize = 256; // Size should be around the max number of chars/string * Encoding's max bytes/char

    public readonly Span<byte> Span;
    private readonly Encoding _encoding;
    private readonly Encoder _encoder;

    //private readonly int _maxChars;
    private readonly char[] _singleChar;
    // private readonly byte[] _largeByteBuffer; // temp space for writing chars.
    private readonly byte[] _buffer; // temp space for writing primitives to.

    public int Length;
    public int Position;

    public SpanWriter(Span<byte> span) : this(span, new UTF8Encoding())
    {
    }

    public SpanWriter(Span<byte> span, Encoding encoding)
    {
        Span = span;
        _encoding = encoding;
        _encoder = encoding.GetEncoder();
        Length = span.Length;
        Position = 0;

        //_largeByteBuffer = new byte[LargeByteBufferSize];
        //_maxChars = _largeByteBuffer.Length / _encoding.GetMaxByteCount(1);
        _singleChar = new char[1];
        _buffer = new byte[BufferLength];
    }

    public byte[] ToArray()
        => Span[..Position].ToArray();

    public int Write(byte value, int? position = null)
    {
        Span[position ?? Position] = value;
        return UpdatePosition(1, position);
    }

    public int Write(string value, Encoding encoding, int? position = null)
    {
        var written = encoding.GetBytes(value, Span[Position..]);

        return UpdatePosition(written, position);
    }

    public int Write(ReadOnlySpan<byte> byteSpan, int? position = null) => Write(byteSpan, byteSpan.Length, position);

    public int Write(ReadOnlySpan<byte> byteSpan, int length, int? position = null)
    {
        byteSpan.CopyTo(Span[(position ?? Position)..]);

        return UpdatePosition(length, position);
    }

    public int Write(char value, int? position = null)
    {
        _singleChar[0] = value;

        var numBytes = _encoder.GetBytes(_singleChar, 0, 1, _buffer, 0, true);
        return Write(_buffer, numBytes, position);
    }

    public int Write(char[] chars, int? position = null)
    {
        byte[] bytes = _encoding.GetBytes(chars, 0, chars.Length);
        return Write(bytes, position);
    }

    public int Write(string value, int? position = null)
    {
        byte[] bytes = _encoding.GetBytes(value, 0, value.Length);
        return Write(bytes, position);
    }

    public int Write(decimal value, int? position = null) => Write(DecimalToBytes(value), position);

    public int Write(DateTime value, int? position = null) => Write(value.ToBinary(), position);

    public int Write(Guid value, int? position = null) => Write(value.ToByteArray(), position);

    public int Write(short num, int? position = null, bool isLittleEndian = false)
    {
        const int numSize = sizeof(short);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        if (isLittleEndian)
            BinaryPrimitives.WriteInt16LittleEndian(span, num);
        else
            BinaryPrimitives.WriteInt16BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(ushort num, int? position = null, bool isLittleEndian = false)
    {
        const int numSize = sizeof(ushort);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        if (isLittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(span, num);
        else
            BinaryPrimitives.WriteUInt16BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(int num, int? position = null, bool isLittleEndian = false)
    {
        const int numSize = sizeof(int);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        if (isLittleEndian)
            BinaryPrimitives.WriteInt32LittleEndian(span, num);
        else
            BinaryPrimitives.WriteInt32BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(uint num, int? position = null, bool isLittleEndian = false)
    {
        const int numSize = sizeof(uint);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        if (isLittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(span, num);
        else
            BinaryPrimitives.WriteUInt32BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(long num, int? position = null)
    {
        const int numSize = sizeof(long);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        BinaryPrimitives.WriteInt64BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(ulong num, int? position = null)
    {
        const int numSize = sizeof(ulong);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        BinaryPrimitives.WriteUInt64BigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(double num, int? position = null)
    {
        const int numSize = sizeof(double);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        BinaryPrimitives.WriteDoubleBigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    public int Write(float num, int? position = null)
    {
        const int numSize = sizeof(float);
        var start = position ?? Position;
        var end = start + numSize;
        var span = Span[start..end];
        BinaryPrimitives.WriteSingleBigEndian(span, num);

        return UpdatePosition(numSize, position);
    }

    private byte[] DecimalToBytes(decimal number)
    {
        var decimalBits = decimal.GetBits(number);

        var lo = decimalBits[0];
        var mid = decimalBits[1];
        var hi = decimalBits[2];
        var flags = decimalBits[3];

        _buffer[0] = (byte)lo;
        _buffer[1] = (byte)(lo >> 8);
        _buffer[2] = (byte)(lo >> 16);
        _buffer[3] = (byte)(lo >> 24);

        _buffer[4] = (byte)mid;
        _buffer[5] = (byte)(mid >> 8);
        _buffer[6] = (byte)(mid >> 16);
        _buffer[7] = (byte)(mid >> 24);

        _buffer[8] = (byte)hi;
        _buffer[9] = (byte)(hi >> 8);
        _buffer[10] = (byte)(hi >> 16);
        _buffer[11] = (byte)(hi >> 24);

        _buffer[12] = (byte)flags;
        _buffer[13] = (byte)(flags >> 8);
        _buffer[14] = (byte)(flags >> 16);
        _buffer[15] = (byte)(flags >> 24);

        return _buffer;
    }

    // Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs#L466
    public int Write7BitEncodedInt(int value, int? position = null)
    {
        int bytesWritten = 0;
        uint uValue = (uint)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            bytesWritten += Write((byte)(uValue | ~0x7Fu), position);
            uValue >>= 7;
        }

        return bytesWritten + Write((byte)uValue, position);
    }

    // Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryWriter.cs#L485
    public int Write7BitEncodedInt64(long value, int? position = null)
    {
        int bytesWritten = 0;
        ulong uValue = (ulong)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            bytesWritten += Write((byte)((uint)uValue | ~0x7Fu), position);
            uValue >>= 7;
        }

        return bytesWritten + Write((byte)uValue, position);
    }

    #region VInt
    public int Write(VInt vint, int? position = null)
    {
        int p = (int)vint.Length;
        for (var data = vint.EncodedValue; --p >= 0; data >>= 8)
        {
            Span[(position ?? Position) + p] = (byte)(data & 0xff);
        }
        return UpdatePosition((int)vint.Length, position);
    }

    public int WriteVInt(ulong value, int? position = null)
    {
        var vint = VInt.EncodeSize(value);
        return Write(vint, position);
    }
    #endregion

    private int UpdatePosition(int length, int? position)
    {
        // Only update the Position during a "normal" Write, else keep it the same.
        if (position is null)
        {
            Position += length;
        }

        return length;
    }
}
