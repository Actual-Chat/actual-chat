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


    // NOTE(AY): Doesn't handle long.MinValue!
    public static int ParseInt(ReadOnlySpan<char> span)
        => TryParseInt(span, out int n) ? n : throw StandardError.Format<int>(span.ToString());
    // NOTE(AY): Doesn't handle long.MinValue!
    public static bool TryParseInt(ReadOnlySpan<char> span, out int number)
    {
        number = 0;
        if (span.Length == 0)
            return false;
        if (span[0] != '-')
            return TryParsePositiveInt(span, out number);

        var result = TryParsePositiveInt(span[1..], out number);
        number = -number;
        return result;
    }

    public static int ParsePositiveInt(ReadOnlySpan<char> span)
        => TryParsePositiveInt(span, out int n) ? n : throw StandardError.Format<int>(span.ToString());
    public static bool TryParsePositiveInt(ReadOnlySpan<char> span, out int number)
    {
        const int maxPreMul = int.MaxValue / 10;
        unchecked {
            number = 0;
            if (span.Length == 0)
                return false;

            foreach (var c in span) {
                if (number > maxPreMul) {
                    number = 0;
                    return false;
                }

                var digit = c - '0';
                if (digit is < 0 or > 9) {
                    number = 0;
                    return false;
                }

                number = (number * 10) + digit;
                if (number < 0) {
                    number = 0;
                    return false;
                }
            }
            return true;
        }
    }

    // NOTE(AY): Doesn't handle long.MinValue!
    public static long ParseLong(ReadOnlySpan<char> span)
        => TryParseLong(span, out long n) ? n : throw StandardError.Format<int>(span.ToString());
    // NOTE(AY): Doesn't handle long.MinValue!
    public static bool TryParseLong(ReadOnlySpan<char> span, out long number)
    {
        number = 0;
        if (span.Length == 0)
            return false;
        if (span[0] != '-')
            return TryParsePositiveLong(span, out number);

        var result = TryParsePositiveLong(span[1..], out number);
        number = -number;
        return result;
    }

    public static long ParsePositiveLong(ReadOnlySpan<char> span)
        => TryParsePositiveLong(span, out long n) ? n : throw StandardError.Format<int>(span.ToString());
    public static bool TryParsePositiveLong(ReadOnlySpan<char> span, out long number)
    {
        const long maxPreMul = long.MaxValue / 10;
        unchecked {
            number = 0L;
            if (span.Length == 0)
                return false;

            foreach (var c in span) {
                if (number > maxPreMul) {
                    number = 0;
                    return false;
                }

                var digit = c - '0';
                if (digit is < 0 or > 9) {
                    number = 0;
                    return false;
                }

                number = (number * 10) + digit;
                if (number < 0) {
                    number = 0;
                    return false;
                }
            }
            return true;
        }
    }
}
