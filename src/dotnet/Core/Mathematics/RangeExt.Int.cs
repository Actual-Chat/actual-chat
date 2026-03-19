namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Size(this Range<int> range)
        => range.End - range.Start;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Move(this Range<int> range, int offset)
        => new(range.Start + offset, range.End + offset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Move(this Range<int> range, int startOffset, int endOffset)
        => new (range.Start + startOffset, range.End + endOffset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Move(this Range<int> range, Range<int> moveTo)
        => new (range.Start + moveTo.Start, range.End + moveTo.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Expand(this Range<int> range, int offset)
        => new(range.Start - offset, range.End + offset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Expand(this Range<int> range, Range<int> expandBy)
        => new (range.Start - expandBy.Start, range.End + expandBy.End);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> Resize(this Range<int> range, int size)
        => new(range.Start, range.Start + size);

    public static bool Contains(this Range<int> range, int value)
        => range.Start <= value && value < range.End;
    public static bool Contains(this Range<int> range, Range<int> containedRange)
        => range.Start <= containedRange.Start && containedRange.End <= range.End;

    public static bool Overlaps(this Range<int> range, Range<int> otherRange)
        => !range.IntersectWith(otherRange).IsEmptyOrNegative;

    public static Range<int> MinMaxWith(this Range<int> range, Range<int> other)
        => (Math.Min(range.Start, other.Start), Math.Max(range.End, other.End));
    public static Range<int> MinMaxWith(this Range<int> range, int point)
        => (Math.Min(range.Start, point), Math.Max(range.End, point));

    public static Range<int> IntersectWith(this Range<int> range, Range<int> other)
    {
        var result = new Range<int>(Math.Max(range.Start, other.Start), Math.Min(range.End, other.End));
        return result.Positive();
    }

    public static Range<int> ExpandToTiles(this Range<int> range, TileLayer<int> tiles)
    {
        var startTile = tiles.GetTile(range.Start);
        var endTile = tiles.GetTile(range.End - 1);
        range = new Range<int>(startTile.Start, endTile.End);
        if (range.IsNegative)
            range = new Range<int>(range.Start, range.Start);
        return range;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<int> ToIntRange(this Range<string> range)
        => new (
            int.Parse(range.Start, NumberStyles.Integer),
            int.Parse(range.End, NumberStyles.Integer));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Range<long> AsLongRange(this Range<int> range)
        => new (range.Start, range.End);
}
