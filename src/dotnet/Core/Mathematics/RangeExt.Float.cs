namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static Range<float> Move(this Range<float> range, float startOffset, float endOffset)
        => new(range.Start + startOffset, range.End + endOffset);

    public static (Range<float> FirstHalf, Range<float> SecondHalf) SplitEvenly(this Range<float> range)
    {
        var splitBoundary = (range.End + range.Start) / 2;
        return ((range.Start, splitBoundary), (splitBoundary, range.End));
    }

    public static Range<float> FitInto(this Range<float> range, Range<float> fitRange)
    {
        var maxSize = Math.Min(range.Size(), fitRange.Size());
        return range.Resize(maxSize).ScrollInto(fitRange);
    }

    public static Range<float> ScrollInto(this Range<float> range, Range<float> fitRange, bool isEndAligned = false)
    {
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
