namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static Range<long> ToInclusive(this Range<long> range)
        => new(range.Start, range.End - 1);

    public static Range<long> ToExclusive(this Range<long> range)
        => new(range.Start, range.End + 1);

    public static Range<long> Move(this Range<long> range, long startOffset, long endOffset)
        => new(range.Start + startOffset, range.End + endOffset);

    public static (Range<long> FirstHalf, Range<long> SecondHalf) SplitEvenly(this Range<long> range)
    {
        var splitBoundary = (range.End + range.Start) >> 1;
        return ((range.Start, splitBoundary), (splitBoundary, range.End));
    }

    public static Range<long> FitInto(this Range<long> range, Range<long> fitRange)
    {
        var maxSize = Math.Min(range.Size(), fitRange.Size());
        return range.Resize(maxSize).ScrollInto(fitRange);
    }

    public static Range<long> ScrollInto(this Range<long> range, Range<long> fitRange)
    {
        var size = range.Size();
        if (range.End > fitRange.End)
            range = (fitRange.End - size, fitRange.End);
        if (range.Start < fitRange.Start)
            range = (fitRange.Start, size);
        return range;
    }
}
