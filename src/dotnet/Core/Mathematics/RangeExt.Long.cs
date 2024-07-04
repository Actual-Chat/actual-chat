using System.Text.RegularExpressions;

namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    [GeneratedRegex("^\\d+")]
    private static partial Regex FirstDigitsRegexFactory();
    private static readonly Regex FirstDigitsRegex = FirstDigitsRegexFactory();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> Move(this Range<long> range, long startOffset, long endOffset)
        => new (range.Start + startOffset, range.End + endOffset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> Move(this Range<long> range, Range<int> moveTo)
        => new (range.Start + moveTo.Start, range.End + moveTo.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> MoveStart(this Range<long> range, long moveStartBy)
        => new (range.Start + moveStartBy, range.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> WithStart(this Range<long> range, long newStart)
        => new (newStart, range.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> MoveEnd(this Range<long> range, long moveEndBy)
        => new (range.Start, range.End + moveEndBy);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> Expand(this Range<long> range, Range<long> expandBy)
        => new (range.Start - expandBy.Start, range.End + expandBy.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> Clamp(this Range<long> range, Range<long> clampBy)
        => new (Math.Clamp(range.Start, clampBy.Start, clampBy.End), Math.Clamp(range.End, clampBy.Start, clampBy.End));

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

    public static Range<long> ScrollInto(this Range<long> range, Range<long> fitRange, bool isEndAligned = false)
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

    public static (Range<long>, Range<long>) Subtract(this Range<long> range, Range<long> subtraction)
    {
        var intersection = range.IntersectWith(subtraction);
        return intersection.IsEmpty
            ? (range, (range.End, range.End))
            : ((range.Start, intersection.Start), (intersection.End, range.End));
    }

    public static Range<long> ExpandToTiles(this Range<long> range, TileLayer<long> tiles)
    {
        var startTile = tiles.GetTile(range.Start);
        var endTile = tiles.GetTile(range.End - 1);
        range = new Range<long>(startTile.Start, endTile.End);
        if (range.IsNegative)
            range = new Range<long>(range.Start, range.Start);
        return range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> ToLongRange(this Range<string> range)
        => new (
            long.Parse(range.Start, NumberStyles.Integer, CultureInfo.InvariantCulture),
            long.Parse(range.End, NumberStyles.Integer, CultureInfo.InvariantCulture));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> ToLongRange(this Range<string> range, bool useFirstDigits)
    {
        if (!useFirstDigits)
            return range.ToLongRange();

        var startMatch = FirstDigitsRegex.Match(range.Start);
        var endMatch = FirstDigitsRegex.Match(range.End);
        if (!startMatch.Success || !endMatch.Success)
            throw StandardError.Constraint($"Unable to parse digit prefix. Range={range}.");

        return new Range<long>(
            long.Parse(startMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            long.Parse(endMatch.Value, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }
}
