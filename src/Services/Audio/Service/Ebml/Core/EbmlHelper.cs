using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ActualChat.Audio.Ebml.Models;

namespace ActualChat.Audio.Ebml
{
    public static class EbmlHelper
    {
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
    }
}