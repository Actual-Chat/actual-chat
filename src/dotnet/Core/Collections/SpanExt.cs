namespace ActualChat.Collections;

public static class SpanExt
{
    public static void WriteIncreasingNumberSequence(this Span<int> span, int start = 0)
    {
        if (start == 0)
            for (var i = 0; i < span.Length; i++)
                span[i] = i;
        else
            for (var i = 0; i < span.Length; i++)
                span[i] = start + i;
    }

    public static void WriteRandomNumberSequence(this Span<int> span, Random random, int start = 0)
    {
        span.WriteIncreasingNumberSequence(start);
        span.Shuffle(random);
    }

    // Fisher–Yates shuffle: https://en.wikipedia.org/wiki/Fisher%E2%80%93Yates_shuffle
    public static void Shuffle<T>(this Span<T> span, Random random)
    {
        var n = span.Length;
        while (n > 1) {
            n--;
            int k = random.Next(n + 1);
            (span[k], span[n]) = (span[n], span[k]);
        }
    }

    public static T GetRandom<T>(this ReadOnlySpan<T> span)
        => span[Random.Shared.Next(span.Length)];
    public static T GetRandom<T>(this ReadOnlySpan<T> span, Random random)
        => span[random.Next(span.Length)];

    /// <summary>
    /// Return 0 <= i <= span.Length such that "!predicate(span[i - 1]) && predicate(span[i])"
    /// </summary>
    public static int BinarySearch<T>(this ReadOnlySpan<T> span, Func<T, bool> predicate)
    {
        int low = -1;
        int high = span.Length;

        while (1 + low < high) {
            var mid = low + ((high - low) >> 1);
            if (predicate(span[mid]))
                high = mid;
            else
                low = mid;
        }
        if (high == span.Length)
            return -1;

        return high;
    }
}
