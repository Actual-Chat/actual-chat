using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    public static class EbmlHelper
    {
        private const ulong UnknownSize = 0xFF_FFFF_FFFF_FFFF;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(ulong value)
        {
            ulong length = 1;
            if ((value & 0xFFFFFFFF00000000) != 0)
            {
                length += 4;
                value >>= 32;
            }
            if ((value & 0xFFFF0000) != 0)
            {
                length += 2;
                value >>= 16;
            }
            if ((value & 0xFF00) != 0) length++;
            return length;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(long value)
        {
            ulong v = (ulong)value;
            if (value < 0)
                v = ~v;
            ulong length = 1;
            if ((v & 0xFFFFFFFF00000000) != 0)
            {
                length += 4;
                v >>= 32;
            }
            if ((v & 0xFFFF0000) != 0)
            {
                length += 2;
                v >>= 16;
            }
            if ((v & 0xFF00) != 0)
            {
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(double value)
        {
            return 8;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(float value)
        {
            return 4;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(DateTime value)
        {
            return 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetSize(string value, bool isAscii)
        {
            var encoding = isAscii 
                ? Encoding.ASCII
                : Encoding.UTF8;
            return (ulong)encoding.GetByteCount(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, ulong value)
        {
            return GetSize(identifier) + GetSize(value) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, ulong? value)
        {
            return value.HasValue ? GetElementSize(identifier, value.Value) : 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, long? value)
        {
            return value.HasValue ? GetElementSize(identifier, value.Value) : 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, long value)
        {
            return GetSize(identifier) + GetSize(value) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, double value)
        {
            return GetSize(identifier) + GetSize(value) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, double? value)
        {
            return value.HasValue ? GetElementSize(identifier, value.Value) : 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, float value)
        {
            return GetSize(identifier) + GetSize(value) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, float? value)
        {
            return value.HasValue ? GetElementSize(identifier, value.Value) : 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, DateTime value)
        {
            return GetSize(identifier) + GetSize(value) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, DateTime? value)
        {
            return value.HasValue ? GetElementSize(identifier, value.Value) : 0;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, byte[]? value)
        {
            return value == null ? 0UL : GetSize(identifier) + (ulong)value.Length + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetElementSize(ulong identifier, string? value, bool isAscii)
        {
            return value == null ? 0UL : GetSize(identifier) + GetSize(value, isAscii) + 1;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetMasterElementSize(ulong identifier, ulong size)
        {
            return GetSize(identifier) + GetCodedSize(size);
        }

        public static ulong GetSize(this IReadOnlyList<BaseModel>? list)
        {
            return list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;
        }
        
        public static ulong GetSize(this IReadOnlyList<Block>? list)
        {
            return list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;
        }
        
        public static ulong GetSize(this IReadOnlyList<SimpleTag>? list)
        {
            return list?.Aggregate(0UL, (size, m) => size + m.GetSize()) ?? 0UL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, ulong? value, ref SpanWriter writer)
        {
            return value == null || WriteEbmlElement(identifier, value.Value, ref writer);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, long? value, ref SpanWriter writer)
        {
            return value == null || WriteEbmlElement(identifier, value.Value, ref writer);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, DateTime value, ref SpanWriter writer)
        {
            var totalSize = GetElementSize(identifier, value);
            if (writer.Position + (int)totalSize > writer.Length)
                return false;

            return WriteEbmlElement(identifier, value.Ticks, ref writer);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, DateTime? value, ref SpanWriter writer)
        {
            return value == null || WriteEbmlElement(identifier, value.Value, ref writer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, double? value, ref SpanWriter writer)
        {
            return value == null || WriteEbmlElement(identifier, value.Value, ref writer);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, float value, ref SpanWriter writer)
        {
            var totalSize = GetElementSize(identifier, value);
            if (writer.Position + (int)totalSize > writer.Length)
                return false;

            writer.Write(VInt.FromEncoded(identifier));
            writer.Write(VInt.EncodeSize(GetSize(value)));
            writer.Write(value);

            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, float? value, ref SpanWriter writer)
        {
            return value == null || WriteEbmlElement(identifier, value.Value, ref writer);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, string? value, bool isAscii, ref SpanWriter writer)
        {
            if (value == null)
                return true;

            var size = GetSize(value, isAscii);
            var totalSize =  GetSize(identifier) + size + 1;
            if (writer.Position + (int)totalSize > writer.Length)
                return false;

            writer.Write(VInt.FromEncoded(identifier));
            writer.Write(VInt.EncodeSize(size));

            var encoding = isAscii ? Encoding.ASCII : Encoding.UTF8;
            writer.Write(value, encoding);

            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteEbmlElement(ulong identifier, byte[]? value, ref SpanWriter writer)
        {
            if (value == null)
                return true;

            var size = GetElementSize(identifier, value);
            var totalSize =  GetSize(identifier) + size + 1;
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
                if (!model?.Write(ref writer) ?? true)
                    return false;

            return true;
        }
        
        public static bool Write(this IReadOnlyList<Block>? blocks, ref SpanWriter writer)
        {
            if (blocks == null)
                return true;

            foreach (var block in blocks)
                if (!block?.Write(ref writer) ?? true)
                    return false;

            return true;
        }
        
        public static bool Write(this IReadOnlyList<SimpleTag>? tags, ref SpanWriter writer)
        {
            if (tags == null)
                return true;

            foreach (var tag in tags)
                if (!tag?.Write(ref writer) ?? true)
                    return false;

            return true;
        }
    }
}