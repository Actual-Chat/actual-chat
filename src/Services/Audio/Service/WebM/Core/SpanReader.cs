using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ActualChat.Audio.WebM
{
    public ref struct SpanReader
    {
        private const int SizeOfGuid = 16;
        private const int MaxCharBytesSize = 128;

        private readonly ReadOnlySpan<byte> _currentSpan;
        private readonly Encoding _encoding;
        private readonly Decoder _decoder;
        private readonly bool _has2BytesPerChar;
        private readonly byte[] _charBytes;
        private readonly char[] _singleChar;
        private readonly int[] _decimalBits;

        public int Length;
        public int Position;

        public SpanReader(ReadOnlySpan<byte> span) : this(span, new UTF8Encoding())
        {
        }

        public SpanReader(ReadOnlySpan<byte> span, Encoding encoding)
        {
            Length = span.Length;
            Position = 0;
            _currentSpan = span;

            _encoding = encoding;
            _decoder = encoding.GetDecoder();

            _has2BytesPerChar = encoding is UnicodeEncoding;
            _charBytes = new byte[MaxCharBytesSize];
            _singleChar = new char[1];
            _decimalBits = new int[4];
        }

        public ReadOnlySpan<byte> Tail => _currentSpan[Position..];

        public ReadOnlySpan<byte> ReadSpan(int length)
        {
            var span = _currentSpan[Position..(Position + length)];

            Position += length;
            
            return  span;
        }

        public bool ReadBoolean() => ReadByte() != 0;

        public byte ReadByte()
        {
            var result = _currentSpan[Position];
            Position += sizeof(byte);
            return result;
        }

        public sbyte ReadSByte() => (sbyte)ReadByte();

        public char ReadChar() => (char)InternalReadOneChar();

        public short ReadShort() => Read<short>();

        public short ReadInt16() => ReadShort();

        public int ReadInt() => ReadInt32();

        public ushort ReadUShort() => Read<ushort>();

        public uint ReadUInt() => ReadUInt32();

        public uint ReadUInt32() => Read<uint>();

        public int ReadInt32() => Read<int>();

        public long ReadLong() => Read<long>();

        public ulong ReadULong() => Read<ulong>();

        
        
        public double ReadFloat(int size)
        {
            var num = ReadULong(size);
            switch (size)
            {
                case 4:
                    return new Union { ULong = num }.Float;
                case 8:
                    return new Union { ULong = num }.Double;
                default:
                    throw new EbmlDataFormatException("Incorrect float length");
            }
        }
        
        public long ReadLong(int size)
        {
            long result = _currentSpan[Position];
            for (var i = 1; i < size; i++) result = (result << 8) | (uint)(_currentSpan[Position + i] & 0xFF);
            
            Position += size;
            
            return result;
        }

        public ulong ReadULong(int size)
        {
            ulong result = 0;
            for (var i = 0; i < size; i++) result = (result << 8) | _currentSpan[Position + i];
            
            Position += size;
            
            return result;
        }

        public string ReadAsciiString(int size) => ReadString(Encoding.ASCII, size);
        
        public string ReadUtf8String(int size) => ReadString(Encoding.UTF8, size);
        
       
        public decimal ReadDecimal()
        {
            var length = sizeof(decimal);
            var buffer = _currentSpan.Slice(Position, length);

            _decimalBits[0] = buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24);
            _decimalBits[1] = buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24);
            _decimalBits[2] = buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24);
            _decimalBits[3] = buffer[12] | (buffer[13] << 8) | (buffer[14] << 16) | (buffer[15] << 24);

            Position += length;

            return new decimal(_decimalBits);
        }

        public float ReadSingle() => ReadFloat();

        public float ReadFloat() => Read<float>();

        public double ReadDouble() => Read<double>();

        public byte[] ReadBytes(int length)
        {
            var result = _currentSpan.Slice(Position, length).ToArray();
            Position += length;
            return result;
        }

        public int ReadBytes(Span<byte> span, int length) => ReadBytes(span, 0, length);

        public int ReadBytes(Span<byte> span, int start, int length)
        {
            _currentSpan.Slice(Position + start, length).CopyTo(span);
            Position += length;
            return length;
        }

        public string ReadString()
        {
            var stringLength = Read7BitEncodedInt();
            var stringBytes = ReadBytes(stringLength);

            return _encoding.GetString(stringBytes);
        }

        public DateTime ReadDateTime()
        {
            var utcNowAsLong = ReadLong();
            return DateTime.FromBinary(utcNowAsLong);
        }

        public Guid ReadGuid()
        {
            return new Guid(ReadBytes(SizeOfGuid));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read<T>() where T : unmanaged
        {
            var newSpan = _currentSpan.Slice(Position);
            var result = MemoryMarshal.Read<T>(newSpan);
            Position += Unsafe.SizeOf<T>();

            return result;
        }

        // Copied from https://referencesource.microsoft.com/#mscorlib/system/io/binaryreader.cs,582
        public int Read7BitEncodedIntOld()
        {
            // Read out an Int32 7 bits at a time.
            // The high bit of the byte when 'on' means to continue reading more bytes.
            int count = 0;
            int shift = 0;
            byte b;
            do
            {
                // Check for a corrupted stream. Read a max of 5 bytes.
                if (shift == 5 * 7) // 5 bytes max per Int32, shift += 7
                {
                    throw new FormatException("Too many bytes in what should have been a 7 bit encoded Int32.");
                }

                // ReadByte handles end of stream cases for us.
                b = ReadByte();
                count |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return count;
        }

        // Based on https://github.com/dotnet/runtime/blob/1d9e50cb4735df46d3de0cee5791e97295eaf588/src/libraries/System.Private.CoreLib/src/System/IO/BinaryReader.cs#L590
        public int Read7BitEncodedInt()
        {
            // Unlike writing, we can't delegate to the 64-bit read on
            // 64-bit platforms. The reason for this is that we want to
            // stop consuming bytes if we encounter an integer overflow.

            uint result = 0;
            byte byteReadJustNow;

            // Read the integer 7 bits at a time. The high bit
            // of the byte when on means to continue reading more bytes.
            //
            // There are two failure cases: we've read more than 5 bytes,
            // or the fifth byte is about to cause integer overflow.
            // This means that we can read the first 4 bytes without
            // worrying about integer overflow.

            const int MaxBytesWithoutOverflow = 4;
            for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                // ReadByte handles end of stream cases for us.
                byteReadJustNow = ReadByte();
                result |= (byteReadJustNow & 0x7Fu) << shift;

                if (byteReadJustNow <= 0x7Fu)
                {
                    return (int)result; // early exit
                }
            }

            // Read the 5th byte. Since we already read 28 bits,
            // the value of this byte must fit within 4 bits (32 - 28),
            // and it must not have the high bit set.

            byteReadJustNow = ReadByte();
            if (byteReadJustNow > 0b_1111u)
            {
                throw new FormatException("Too many bytes in what should have been a 7-bit encoded integer.");
            }

            result |= (uint)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
            return (int)result;
        }

        public long Read7BitEncodedInt64()
        {
            ulong result = 0;
            byte byteReadJustNow;

            // Read the integer 7 bits at a time. The high bit
            // of the byte when on means to continue reading more bytes.
            //
            // There are two failure cases: we've read more than 10 bytes,
            // or the tenth byte is about to cause integer overflow.
            // This means that we can read the first 9 bytes without
            // worrying about integer overflow.

            const int MaxBytesWithoutOverflow = 9;
            for (int shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                // ReadByte handles end of stream cases for us.
                byteReadJustNow = ReadByte();
                result |= (byteReadJustNow & 0x7Ful) << shift;

                if (byteReadJustNow <= 0x7Fu)
                {
                    return (long)result; // early exit
                }
            }

            // Read the 10th byte. Since we already read 63 bits,
            // the value of this byte must fit within 1 bit (64 - 63),
            // and it must not have the high bit set.

            byteReadJustNow = ReadByte();
            if (byteReadJustNow > 0b_1u)
            {
                throw new FormatException("Too many bytes in what should have been a 7-bit encoded integer.");
            }

            result |= (ulong)byteReadJustNow << (MaxBytesWithoutOverflow * 7);
            return (long)result;
        }
        
        public bool Skip(int intLength)
        {
            if (Position + intLength > Length)
                return false;
            
            Position += intLength;
            return true;
        }

        public (bool, VInt) ReadVInt(int maxLength = 4)
        {
            uint b1 = ReadByte();
            ulong raw = b1;
            uint mask = 0xFF00;

            for (int i = 0; i < maxLength; ++i)
            {
                mask >>= 1;

                if ((b1 & mask) == 0) continue;
                
                ulong value = raw & ~mask;

                for (int j = 0; j < i; ++j)
                {
                    byte b = ReadByte();

                    raw = (raw << 8) | b;
                    value = (value << 8) | b;
                }

                return (true, VInt.EncodeSize(raw, i + 1));
            }

            return (false, VInt.Unknown);
        }
        
        private string ReadString(Encoding decoder, int size)
        {
            if (size == 0)
                return string.Empty;

            var result = decoder.GetString(_currentSpan.Slice(Position, size));

            Position += size;
            return result;
        }
        

        // Copied from https://referencesource.microsoft.com/#mscorlib/system/io/binaryreader.cs,409
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int InternalReadOneChar()
        {
            // I know having a separate InternalReadOneChar method seems a little redundant,
            // but this makes a scenario like the security parser code 20% faster, in addition to the optimizations for UnicodeEncoding I put in InternalReadChars.   
            int charsRead = 0;

            while (charsRead == 0)
            {
                // We really want to know what the minimum number of bytes per char
                // is for our encoding. Otherwise for UnicodeEncoding we'd have to do ~1+log(n) reads to read n characters.
                // Assume 1 byte can be 1 char unless _has2BytesPerChar is true.
                int numBytes = _has2BytesPerChar ? 2 : 1;

                int r = ReadByte();
                _charBytes[0] = (byte)r;

                if (r == -1)
                {
                    numBytes = 0;
                }

                if (numBytes == 2)
                {
                    r = ReadByte();
                    _charBytes[1] = (byte)r;

                    if (r == -1)
                    {
                        numBytes = 1;
                    }
                }

                if (numBytes == 0)
                {
                    throw new Exception("Found no bytes. We're outta here.");
                }

                charsRead = _decoder.GetChars(_charBytes, 0, numBytes, _singleChar, 0);
            }

            if (charsRead == 0)
            {
                return -1;
            }

            return _singleChar[0];
        }
        
        [StructLayout(LayoutKind.Explicit)]
        private struct Union
        {
            [FieldOffset(0)]
            public uint UInt;
            [FieldOffset(0)]
            public float Float;

            [FieldOffset(0)]
            public ulong ULong;
            [FieldOffset(0)]
            public double Double;
        }
    }
}