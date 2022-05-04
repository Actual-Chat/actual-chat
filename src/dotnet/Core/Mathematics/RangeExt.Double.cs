namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static Range<double> Move(this Range<double> range, double startOffset, double endOffset)
        => new(range.Start + startOffset, range.End + endOffset);

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
