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
 * */

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ActualChat.Audio.Ebml.Backup
{
    /// <summary>
    /// Writes EBML file
    /// </summary>
    public class EbmlWriter
    {
        protected readonly Stream _stream;

        /// <summary>
        /// Initializes a new instance of the EbmlWriter class
        /// </summary>
        /// <param name="stream"></param>
        public EbmlWriter(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        // /// <summary>
        // /// Starts nested stream. Upon disposal of nested stream, the size is calculated and written to nested element header.
        // /// Example:
        // /// using(var data = writer.StartMasterElement(InnerDataElementId))
        // /// {
        // ///     data.WriteInt(DataItem1Id, 139874)
        // ///     data.WriteUtf(DataItem2Id, "Hello world");
        // /// }
        // /// </summary>
        // /// <param name="elementId"></param>
        // /// <returns></returns>
        // public MasterBlockWriter StartMasterElement(VInt elementId)
        // {
        //     return new MasterBlockWriter(this, elementId);
        // }

        /// <summary>
        /// Writes element header
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public int WriteElementHeader(VInt elementId, VInt size)
        {
            return elementId.Write(_stream) + size.Write(_stream);
        }

        #region Writing atomic elements

        /// <summary>
        /// Writes signed integer value
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int Write(VInt elementId, long value)
        {
            var length = value == 0 ? 0u : 1u;
            unchecked
            {
                var mask = (long)0xffffffffffffff80L;
                for (var expected = value < 0L ? mask : 0L; length < 8 && (mask & value) != expected; mask <<= 8, expected <<= 8)
                {
                    length++;
                }
            }

            return elementId.Write(_stream) + EncodeWidth(length).Write(_stream) + WriteInt(value, length);
        }

        /// <summary>
        /// Writes unsigned integer value
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int Write(VInt elementId, ulong value)
        {
            var length = 0u;
            for (var mask = UInt64.MaxValue; (value & mask) != 0 && length < 8; mask <<= 8)
            {
                length++;
            }

            return elementId.Write(_stream) + EncodeWidth(length).Write(_stream) + WriteUInt(value, length);
        }

        /// <summary>
        /// Writes datetime value
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        public int Write(VInt elementId, DateTime dateTime)
        {
            // var d = (dateTime - EbmlReader.MilleniumStart).Ticks * 100;
            // return elementId.Write(_stream) + EncodeWidth(8).Write(_stream) + WriteInt(d, 8);

            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes floating point number
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int Write(VInt elementId, float value)
        {
            var u = new Union {fval = value}.uival;
            return elementId.Write(_stream) + EncodeWidth(4).Write(_stream) + WriteInt(u, 4);
        }

        /// <summary>
        /// Writes floating point number with double precision
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int Write(VInt elementId, double value)
        {
            var u = new Union { dval = value }.ulval;
            return elementId.Write(_stream) + EncodeWidth(8).Write(_stream) + WriteUInt(u, 8);
        }

        /// <summary>
        /// Writes binary data
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public int Write(VInt elementId, byte[] data, int offset, int length)
        {
            var headLen = elementId.Write(_stream) + EncodeWidth((uint) length).Write(_stream);
            _stream.Write(data, offset, length);
            return headLen + length;
        }

        /// <summary>
        /// Writes binary data
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public int Write(VInt elementId, byte[] data)
        {
            return Write(elementId, data, 0, data.Length);
        }

        /// <summary>
        /// Writes string in ASCII encoding
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int WriteAscii(VInt elementId, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var buffer = string.IsNullOrEmpty(value) ? new byte[0] : Encoding.ASCII.GetBytes(value);
            return Write(elementId, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Writes string in UTF-8 encoding
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public int WriteUtf(VInt elementId, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (!value.IsNormalized(NormalizationForm.FormC))
            {
                value = value.Normalize(NormalizationForm.FormC);
            }
            var buffer = string.IsNullOrEmpty(value) ? new byte[0] : Encoding.UTF8.GetBytes(value);
            return Write(elementId, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Writes raw binary data
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public int Write(byte[] buffer, int offset, int length)
        {
            _stream.Write(buffer, offset, length);
            return length;
        }

        #endregion

        #region Implementation

        private int WriteInt(Int64 value, uint length)
        {
            if(length > 8) throw new ArgumentOutOfRangeException(nameof(length));
            if (value == 0 && length == 0) return 0;

            var buffer = new byte[length];

            var p = (int)length;
            for (var data = value; --p >= 0; data >>= 8)
            {
                buffer[p] = (byte)(data & 0xff);
            }

            return Write(buffer, 0, (int)length);
        }

        private int WriteUInt(UInt64 value, uint length)
        {
            if (length > 8) throw new ArgumentOutOfRangeException(nameof(length));
            if (value == 0 && length == 0) return 0;

            var buffer = new byte[length];

            var p = (int)length;
            for (var data = value; --p >= 0; data >>= 8)
            {
                buffer[p] = (byte)(data & 0xff);
            }

            return Write(buffer, 0, (int) length);
        }

        [ThreadStatic] // avoid thread sync code
        private static VInt[]? __smallSizes;

        private const int CachedWidthCount = 20;

        private static VInt EncodeWidth(uint width)
        {
            if (__smallSizes == null)
            {
                __smallSizes = Enumerable.Range(0, CachedWidthCount).Select(i => VInt.EncodeSize((ulong)i)).ToArray();
            }
            return width < CachedWidthCount ? __smallSizes[width] : VInt.EncodeSize(width);
        }

        #endregion
    }
}
