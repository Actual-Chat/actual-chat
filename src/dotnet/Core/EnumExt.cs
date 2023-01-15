namespace ActualChat;

public static class EnumExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format<T>(this T source)
        where T : struct, Enum
        => source.ToUInt64().Format();

    public static ulong ToUInt64<T>(this T source)
        where T : struct, Enum
    {
        ref byte data = ref Unsafe.As<T, byte>(ref source);
        return source.GetTypeCode() switch {
            // Most frequent types go first - just in case
            TypeCode.SByte => (ulong)Unsafe.As<byte, sbyte>(ref data),
            TypeCode.Byte => data,
            TypeCode.Int32 => (ulong)Unsafe.As<byte, int>(ref data),
            TypeCode.UInt32 => Unsafe.As<byte, uint>(ref data),
            TypeCode.Int64 => (ulong)Unsafe.As<byte, long>(ref data),
            TypeCode.UInt64 => Unsafe.As<byte, ulong>(ref data),
            TypeCode.Int16 => (ulong)Unsafe.As<byte, short>(ref data),
            TypeCode.UInt16 => Unsafe.As<byte, ushort>(ref data),
            TypeCode.Char => Unsafe.As<byte, ushort>(ref data),
            TypeCode.Boolean => data != 0 ? 1ul : 0ul,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }
}
