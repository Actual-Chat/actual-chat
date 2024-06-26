namespace ActualChat.Mathematics;

public static partial class RangeExt
{
    public static Range<int> Expand(this Range<int> range, Range<int> expandBy)
        => new (range.Start - expandBy.Start, range.End + expandBy.End);

    public static Range<int> ExpandToTiles(this Range<int> range, TileLayer<int> tiles)
    {
        var startTile = tiles.GetTile(range.Start);
        var endTile = tiles.GetTile(range.End - 1);
        range = new Range<int>(startTile.Start, endTile.End);
        if (range.IsNegative)
            range = new Range<int>(range.Start, range.Start);
        return range;
    }

    public static Range<int> ToIntRange(this Range<string> range)
        => new (
            int.Parse(range.Start, NumberStyles.Integer, CultureInfo.InvariantCulture),
            int.Parse(range.End, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static Range<long> AsLongRange(this Range<int> range)
        => new (range.Start, range.End);
}
