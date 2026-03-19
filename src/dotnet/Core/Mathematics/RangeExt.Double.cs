namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static bool Equals(this Range<double> range, Range<double> otherRange, double epsilon)
        => Math.Abs(range.Start - otherRange.Start) < epsilon
            && Math.Abs(range.End - otherRange.End) < epsilon;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Size(this Range<double> range)
        => range.End - range.Start;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<double> Move(this Range<double> range, double offset)
        => new(range.Start + offset, range.End + offset);
    public static Range<double> Move(this Range<double> range, double startOffset, double endOffset)
        => new(range.Start + startOffset, range.End + endOffset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<double> Expand(this Range<double> range, double offset)
        => new(range.Start - offset, range.End + offset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<double> Resize(this Range<double> range, double size)
        => new(range.Start, range.Start + size);

    public static bool Contains(this Range<double> range, double value)
        => range.Start <= value && value < range.End;
    public static bool Contains(this Range<double> range, Range<double> containedRange)
        => range.Start <= containedRange.Start && containedRange.End <= range.End;

    public static bool Overlaps(this Range<double> range, Range<double> otherRange)
        => !range.IntersectWith(otherRange).IsEmptyOrNegative;

    public static Range<double> MinMaxWith(this Range<double> range, Range<double> other)
        => (Math.Min(range.Start, other.Start), Math.Max(range.End, other.End));
    public static Range<double> MinMaxWith(this Range<double> range, double point)
        => (Math.Min(range.Start, point), Math.Max(range.End, point));

    public static Range<double> IntersectWith(this Range<double> range, Range<double> other)
    {
        var result = new Range<double>(Math.Max(range.Start, other.Start), Math.Min(range.End, other.End));
        return result.Positive();
    }

    public static (Range<double> FirstHalf, Range<double> SecondHalf) SplitEvenly(this Range<double> range)
    {
        var splitBoundary = (range.End + range.Start) / 2;
        return ((range.Start, splitBoundary), (splitBoundary, range.End));
    }

    public static Range<double> FitInto(this Range<double> range, Range<double> fitRange)
    {
        var maxSize = Math.Min(range.Size(), fitRange.Size());
        return range.Resize(maxSize).ScrollInto(fitRange);
    }

    public static Range<double> ScrollInto(this Range<double> range, Range<double> fitRange, bool isEndAligned = false)
    {
        // no need to scroll
        if(range.Contains(fitRange))
            return range;

        var size = range.Size();
        if (isEndAligned) {
            if (range.Start < fitRange.Start)
                range = (fitRange.Start, size);
            if (range.End > fitRange.End)
                range = (fitRange.End - size, fitRange.End);
        }
        else {
            if (range.End > fitRange.End)
                range = (fitRange.End - size, fitRange.End);
            if (range.Start < fitRange.Start)
                range = (fitRange.Start, size);
        }
        return range;
    }
}
