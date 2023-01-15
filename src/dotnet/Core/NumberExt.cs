using System.Numerics;

namespace ActualChat;

public static class NumberExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format<T>(this T source)
        where T : struct, IBinaryInteger<T>
        => source.ToString(null, CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format(this double source)
        => source.ToString(CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format(this float source)
        => source.ToString(CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Format(this Half source)
        => source.ToString(CultureInfo.InvariantCulture);
}
