/* Copyright (c) 2011-2020 Oleg Zee
 * 
 * Original java code Copyright (c) 2008, Oleg S. Estekhin

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
 * */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ActualChat.Audio.Ebml.Backup
{
    /// <summary>
    /// The <code>EbmlReader</code> interface allows forward, read-only access to EBML data.
    /// </summary>
    public sealed class EbmlReader : IDisposable
    {
        private readonly Stack<Element> _containers;
        private readonly Stream _source;
        private Element _container;
        private Element _element;
        private byte[]? _sharedBuffer;

        /// <summary>
        /// Creates a new EBML reader.
        /// </summary>
        /// <param name="source">the source of bytes</param>
        /// <exception cref="ArgumentNullException">if <code>source</code> is <code>null</code></exception>
        public EbmlReader(Stream source) : this(source, long.MaxValue)
        {
        }

        /// <summary>
        /// Creates a new EBML reader.
        /// </summary>
        /// <param name="source">the source of bytes</param>
        /// <param name="size">the maximum number of bytes to read from the source</param>
        /// <exception cref="ArgumentNullException">if <code>source</code> is <code>null</code></exception>
        /// <exception cref="ArgumentNullException">if <code>size</code> is negative</exception>
        public EbmlReader(Stream source, long size)
        {
            if (size < 0L) throw new ArgumentException("size is negative");

            _source = source ?? throw new ArgumentNullException(nameof(source));
            _containers = new Stack<Element>();
            _container = new Element(VInt.UnknownSize(2), size, ElementType.MasterElement);
            _element = Element.Empty;
        }

        #region Public API

        /// <summary>
        /// Reads the next child element of the current container and positions the stream at the beginning of the element data.
        /// </summary>
        /// <returns><code>true</code> if the child element is available; <code>false</code> otherwise</returns>
        /// <exception cref="EbmlDataFormatException">if the value of the element identifier or element data size read from the stream is reserved</exception>
        public bool ReadNext()
        {
            Skip(_element.Remaining);
            _container.Remaining -= _element.Size;
            _element = _container;

            if(_element.Remaining < 1)
            {
                _element = Element.Empty;
                return false;
            }
            
            ReadElement();
            return true;
        }

        /// <summary>
        /// Reads the next child element of the current container at the specified position and positions the stream at the beginning of the element data.
        /// </summary>
        /// <param name="position">The exact position in the current container to read the next element from.</param>
        /// <returns><code>true</code> if the child element is available; <code>false</code> otherwise</returns>
        /// <exception cref="EbmlDataFormatException">if the value of the element identifier or element data size read from the stream is reserved</exception>
        public bool ReadAt(long position)
        {
            _container.Remaining -= _element.Size;
            _element = _container;

            if (_element.Remaining < 1)
            {
                _element = Element.Empty;
                return false;
            }

            // compute the desired position relative to the current position in the container
            var relativePosition = position - (_container.Size - _container.Remaining);

            if (relativePosition < 0)
            {
                throw new EbmlDataFormatException("invalid position, seeking backwards is not supported");
            }

            Skip(relativePosition);

            ReadElement();
            return true;
        }

        /// <summary>
        /// Returns the identifier of the current element.
        /// </summary>
        /// <value>the element identifier</value>
        /// <exception cref="InvalidOperationException">if the current element is not available</exception>
        public VInt ElementId
        {
            get
            {
                if (_element.HasInvalidIdentifier)
                    throw new InvalidOperationException();
                return _element.Identifier;
            }
        }

        /// <summary>
        /// Returns the data size of the current element.
        /// </summary>
        /// <value>the element data size in the encoded form</value>
        /// <exception cref="InvalidOperationException">if the current element is not available</exception>
        public long ElementSize
        {
            get
            {
                if (_element.HasInvalidIdentifier)
                    throw new InvalidOperationException();
                return _element.Size;
            }
        }

        /// <summary>
        /// Gets starting position in file for current element
        /// </summary>
        public long ElementPosition { get; private set; }

        /// <summary>
        /// Instructs the reader to parse the current element data as sub-elements. The current container 
        /// will be saved on the stack and the current element will become the new container.
        /// </summary>
        /// <exception cref="InvalidOperationException">if the current element is not available or if the element data was already accessed as some other type</exception>
        public void EnterContainer()
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            _containers.Push(_container);
            _container = _element;
            _container.Type = ElementType.MasterElement;
            _element = Element.Empty;
        }

        /// <summary>
        /// Instructs the reader to return to the previous container.
        /// </summary>
        /// <exception cref="InvalidOperationException">if the current container represents the whole input source</exception>
        /// <exception cref="EndOfStreamException">if the input source reaches the end before reading all the bytes</exception>
        /// <exception cref="IOException">if an I/O error has occurred</exception>
        public void LeaveContainer()
        {
            if (_containers.Count == 0)
            {
                throw new InvalidOperationException();
            }
            _container.Remaining -= _element.Size;
            _element = _container;
            Skip(_element.Remaining);
            _container = _containers.Pop();
        }

        /// <summary>
        /// Reads the element data as a signed integer.
        /// </summary>
        /// <returns>the element data as a signed integer</returns>
        public long ReadInt()
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            if (_element.Size > 8)
            {
                throw new EbmlDataFormatException("invalid signed integer size");
            }
            _element.Type = ElementType.SignedInteger;
            var encodedValueSize = (int) _element.Size;

            return encodedValueSize == 0 ? 0L : ReadSignedIntegerUnsafe(encodedValueSize);
        }

        /// <summary>
        /// Reads the element data as an unsigned integer.
        /// </summary>
        /// <returns>the element data as an unsigned integer</returns>
        /// <exception cref="InvalidOperationException">if the current element is not available or if the element data was already accessed as some other type</exception>
        /// <exception cref="EbmlDataFormatException">if the element size is greater than <code>8</code></exception>
        public ulong ReadUInt()
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            if (_element.Size > 8)
            {
                throw new EbmlDataFormatException("invalid unsigned integer size");
            }
            _element.Type = ElementType.UnsignedInteger;

            var encodedValueSize = (int) _element.Size;
            return encodedValueSize == 0 ? 0L : ReadUnsignedIntegerUnsafe(encodedValueSize);
        }

        /// <summary>
        /// Reads the element data as a floating-point number.
        /// 
        /// If the element data size is equal to <code>4</code>, then an instance of the <code>Float</code> is returned. If
        /// the element data size is equal to <code>8</code>, then an instance of the <code>Double</code> is returned.
        /// </summary>
        /// <returns>the element data as a floating-point number</returns>
        public double ReadFloat()
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            if (_element.Size != 4 && _element.Size != 8)
            {
                throw new EbmlDataFormatException("invalid float size");
            }
            _element.Type = ElementType.Float;
            var encodedValueSize = (int) _element.Size;
            var num = ReadUnsignedIntegerUnsafe(encodedValueSize);

            switch (encodedValueSize)
            {
                case 4:
                    return new Union {ulval = num}.fval;
                case 8:
                    return new Union { ulval = num }.dval;
                default:
                    throw new EbmlDataFormatException("Incorrect float length");
            }
        }

        /// <summary>
        /// Reads the element data as a date.
        /// </summary>
        /// <returns>the element data as a date</returns>
        public DateTime ReadDate()
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            if (_element.Size != 8)
            {
                throw new EbmlDataFormatException("invalid date size");
            }
            _element.Type = ElementType.Date;
            var ns = ReadSignedIntegerUnsafe(8);

            return MilleniumStart.AddTicks(ns/100);
        }

        internal static readonly DateTime MilleniumStart = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Reads the element data as an ASCII string.
        /// </summary>
        /// <returns>the element data as an ASCII string</returns>
        public string ReadAscii()
        {
            return ReadString(Encoding.ASCII);
        }

        /// <summary>
        /// Reads the element data as an UTF8 string.
        /// </summary>
        /// <returns>the element data as an UTF8 string</returns>
        public string ReadUtf()
        {
            return ReadString(Encoding.UTF8);
        }

        /// <summary>
        /// Reads the element data as binary.
        /// </summary>
        /// <param name="buffer">the buffer into which the data is read</param>
        /// <param name="offset">the start offset in <code>buffer</code> at which the data is written</param>
        /// <param name="length">the maximum number of bytes to read</param>
        /// <returns>the actual number of bytes read, or <code>-1</code> if the end of the element data is reached</returns>
        public int ReadBinary(byte[] buffer, int offset, int length)
        {
            if (_element.HasInvalidIdentifier || _element.Type != ElementType.None && _element.Type != ElementType.Binary)
            {
                throw new InvalidOperationException();
            }
            _element.Type = ElementType.Binary;
            if (_element.Remaining == 0L)
            {
                return -1;
            }
            var r = _source.ReadFully(buffer, offset, (int) Math.Min(_element.Remaining, length));
            if (r < 0)
            {
                throw new EndOfStreamException();
            }
            _element.Remaining -= r;
            return r;
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            _source.Dispose();
            _containers.Clear();
            _container = Element.Empty;
            _element = Element.Empty;
        }

        #endregion

        #region Implementation

        private byte[] GetSharedBuffer(int length)
        {
            if (_sharedBuffer == null || _sharedBuffer.Length < length)
            {
                _sharedBuffer = new byte[Math.Max(2048, length)];
            }
            return _sharedBuffer;
        }

        private byte[] FillBuffer(int length)
        {
            byte[] buffer = GetSharedBuffer(length);
            ReadFully(buffer, 0, length);

            return buffer;
        }

        /// <summary>
        /// Reads <code>length</code> bytes of data from the current element data into an array of bytes.
        /// </summary>
        /// <param name="buffer">the buffer into which the data is read</param>
        /// <param name="offset">the start offset in array <code>buffer</code> at which the data is written</param>
        /// <param name="length">the number of bytes to read</param>
        private void ReadFully(byte[] buffer, int offset, int length)
        {
            if (_element.Remaining < length)
            {
                throw new EndOfStreamException();
            }
            while (length > 0)
            {
                int r = _source.ReadFully(buffer, offset, length);
                if (r < 0)
                {
                    throw new EndOfStreamException();
                }
                offset += r;
                length -= r;
                _element.Remaining -= r;
            }
        }

        /// <summary>
        /// Skips <code>length</code> bytes from an input stream.
        /// </summary>
        /// <param name="length">the number of bytes to skip</param>
        private void Skip(long length)
        {
            if (!_element.IsEmpty && _element.Remaining < length)
            {
                throw new EndOfStreamException();
            }
            long skipped = 1;
            while (length > 0L && skipped > 0L)
            {
                var start = _source.Position;
                var newPos = _source.Seek(length, SeekOrigin.Current);

                skipped = newPos - start;
                length -= skipped;
                _element.Remaining -= skipped;
            }
            while (length > 0L)
            {
                var buffer = GetSharedBuffer(2048);
                var r = _source.ReadFully(buffer, 0, (int) Math.Min(length, buffer.Length));
                if (r < 0)
                {
                    throw new EndOfStreamException();
                }
                length -= r;
                _element.Remaining -= r;
            }
        }

        private void ReadElement()
        {
            ElementPosition = _source.Position;

            var identifier = ReadVarInt(4);

            if (identifier.IsReserved)
            {
                throw new EbmlDataFormatException("invalid element identifier value");
            }

            var size = ReadVarInt(8).Value;
            if (size > (ulong)_container.Remaining)
            {
                // Neither Cluster nor Segment
                if (identifier.Value != 0xF43B675 && identifier.Value != 0x8538067)
                    throw new EbmlDataFormatException("invalid element size value");
            }
            
            _element = new Element(identifier, (long) size, ElementType.None);
        }

        /// <summary>
        /// Reads the element data as a variable size integer.
        /// </summary>
        /// <returns>a variable size integer, or <code>null</code> if the end of the input source is reached</returns>
        /// <exception cref="EbmlDataFormatException">if the input source contains length descriptor with zero value</exception>
        /// <exception cref="EndOfStreamException">if the input source reaches the end before reading all the bytes</exception>
        /// <exception cref="IOException">if an I/O error has occurred</exception>
        private VInt ReadVarInt(int maxLength)
        {
            var value = VInt.Read(_source, maxLength, GetSharedBuffer(maxLength));
            if (_element.Remaining < value.Length)
                throw new EbmlDataFormatException();

            _element.Remaining -= value.Length;
            return value;
        }

        /// <summary>
        /// Reads the element data as a signed integer.
        /// </summary>
        /// <param name="length">the number of bytes to read</param>
        /// <returns>the element data as a signed integer</returns>
        private long ReadSignedIntegerUnsafe(int length)
        {
            var buffer = FillBuffer(length);
            long result = (sbyte)buffer[0]; // with sign extension
            for (var i = 1; i < length; i++)
            {
                result = result << 8 | (uint)(buffer[i] & 0xff);
            }
            return result;
        }

        /// <summary>
        /// Reads the element data as an unsigned integer.
        /// </summary>
        /// <param name="length">the number of bytes to read</param>
        /// <returns>the element data as an unsigned integer</returns>
        private ulong ReadUnsignedIntegerUnsafe(int length)
        {
            var buffer = FillBuffer(length);

            ulong result = 0;
            for (var i = 0; i < length; i++)
            {
                result = (result << 8) | buffer[i];
            }
            return result;
        }

        /// <summary>
        /// Reads the element data as a string in the specified charset.
        /// </summary>
        /// <param name="decoder">the name of the charset to be used to decode the bytes</param>
        /// <returns>the element data as a string</returns>
        private string ReadString(Encoding decoder)
        {
            if (_element.HasInvalidIdentifier || _element.Size != _element.Remaining || _element.Type != ElementType.None)
            {
                throw new InvalidOperationException();
            }
            _element.Type = ElementType.AsciiString; // or UTF_8_STRING, but for the housekeeping it does not matter
            var encodedValueSize = (int) _element.Size;
            if (encodedValueSize == 0)
            {
                return "";
            }

            var buffer = FillBuffer(encodedValueSize);
            while (encodedValueSize > 0 && buffer[encodedValueSize - 1] == 0)
            {
                encodedValueSize--;
            }

            return decoder.GetString(buffer, 0, encodedValueSize);
        }

        #endregion

        #region the element frame information

        private class Element
        {
            public static readonly Element Empty = new Element();

            public readonly VInt Identifier;
            public readonly long Size;

            public bool IsEmpty => !Identifier.IsValidIdentifier && Size == 0 && Type == ElementType.None;

            public bool HasInvalidIdentifier => !Identifier.IsValidIdentifier;

            public long Remaining;

            public ElementType Type;

            public Element(VInt identifier, long sizeValue, ElementType type)
            {
                Identifier = identifier;
                Size = sizeValue;
                Remaining = sizeValue;
                Type = type;
            }

            private Element()
                : this(VInt.UnknownSize(2), 0L, ElementType.None)
            {
            }
        }

        #endregion
    }
}