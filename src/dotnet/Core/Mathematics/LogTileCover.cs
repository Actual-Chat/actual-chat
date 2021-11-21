namespace ActualChat.Mathematics;

public abstract class LogTileCover<TPoint, TSize>
    where TPoint : notnull
    where TSize : notnull
{
    private TSize[]? _rangeSizes;

    public TPoint Zero { get; init; } = default!;
    public TSize MinTileSize { get; init; } = default!;
    public TSize MaxTileSize { get; init; } = default!;
    public int TileSizeFactor { get; init; } = 4;
    public SizeMeasure<TPoint, TSize> Measure { get; init; } = null!;
    public TSize[] TileSizes => _rangeSizes ??= GetTileSizes();

    public void AssertIsTile(Range<TPoint> tile)
    {
        if (!IsTile(tile))
            throw new InvalidOperationException("Invalid tile boundaries.");
    }

    public virtual bool IsTile(Range<TPoint> tile)
        => GetTileSizeIndex(tile) >= 0;

    public virtual int GetTileSizeIndex(Range<TPoint> tile)
    {
        var sizeMeasure = Measure;
        var size = sizeMeasure.GetSize(tile);
        for (var i = 0; i < TileSizes.Length; i++) {
            var rangeSize = TileSizes[i];
            if (EqualityComparer<TSize>.Default.Equals(rangeSize, size)) {
                var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, tile.Start), size);
                if (EqualityComparer<TSize>.Default.Equals(offset, default))
                    return i;
            }
        }
        return -1;
    }

    public virtual Range<TPoint>[] GetSmallerTiles(Range<TPoint> tile)
    {
        var sizeIndex = GetTileSizeIndex(tile);
        if (sizeIndex < 0)
            throw new InvalidOperationException("Invalid tile boundaries.");
        if (sizeIndex == 0)
            return Array.Empty<Range<TPoint>>();

        var sizeMeasure = Measure;
        var smallerSize = TileSizes[sizeIndex - 1];
        var result = new Range<TPoint>[TileSizeFactor];
        var start = tile.Start;
        for (var i = 0; i < TileSizeFactor; i++) {
            var end = sizeMeasure.AddOffset(tile.Start, smallerSize);
            result[i] = (start, end);
            start = end;
        }
        return result;
    }

    public virtual TPoint GetTileStart(TPoint innerPoint, int tileSizeIndex)
    {
        var sizeMeasure = Measure;
        var size = TileSizes[tileSizeIndex];
        var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
        return sizeMeasure.SubtractOffset(innerPoint, offset);
    }

    public virtual TPoint GetTileEnd(TPoint innerPoint, int tileSizeIndex)
    {
        var sizeMeasure = Measure;
        var size = TileSizes[tileSizeIndex];
        var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
        if (EqualityComparer<TSize>.Default.Equals(offset, default))
            return innerPoint;
        return sizeMeasure.AddOffset(sizeMeasure.SubtractOffset(innerPoint, offset), size);
    }

    public virtual Range<TPoint> GetTile(TPoint innerPoint)
    {
        var sizeMeasure = Measure;
        var size = MinTileSize;
        var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
        var start = sizeMeasure.SubtractOffset(innerPoint, offset);
        return new Range<TPoint>(start, sizeMeasure.AddOffset(start, size));
    }

    public virtual IEnumerable<Range<TPoint>> GetTileCover(Range<TPoint> range)
    {
        var equalityComparer = EqualityComparer<TSize>.Default;
        var comparer = Comparer<TSize>.Default;
        var sizeMeasure = Measure;
        range = (GetTileStart(range.Start, 0), GetTileEnd(range.End, 0));
        while (!range.IsEmpty) {
            for (var i = TileSizes.Length - 1; i >= 0; i--) {
                var size = TileSizes[i];
                var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, range.Start), size);
                if (!equalityComparer.Equals(offset, default))
                    continue;
                var end = sizeMeasure.AddOffset(range.Start, size);
                var extraSize = sizeMeasure.GetDistance(range.End, end);
                if (comparer.Compare(extraSize, default) > 0)
                    continue;
                yield return (range.Start, end);
                range = (end, range.End);
                break;
            }
        }
    }

    public virtual IEnumerable<Range<TPoint>> GetAllTiles(TPoint innerPoint)
    {
        var sizeMeasure = Measure;
        foreach (var size in TileSizes) {
            var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
            var start = sizeMeasure.SubtractOffset(innerPoint, offset);
            yield return new Range<TPoint>(start, sizeMeasure.AddOffset(start, size));
        }
    }

    public virtual bool TryGetSmallestCoveringTile(Range<TPoint> range, out Range<TPoint> tile)
    {
        var sizeMeasure = Measure;
        var comparer = Comparer<TSize>.Default;

        var minSize = sizeMeasure.GetSize(range);
        if (comparer.Compare(default, minSize) > 0)
            throw new ArgumentOutOfRangeException(nameof(range));

        // TODO(AY): Use binary search when (RangeSizes.Length >= 8) or so
        for (var i = 0; i < TileSizes.Length; i++) {
            var size = TileSizes[i];
            if (comparer.Compare(size, minSize) >= 0) {
                var start = GetTileStart(range.Start, i);
                // ~ if (start + size >= innerRange.End) ...
                if (comparer.Compare(size, sizeMeasure.GetDistance(start, range.End)) >= 0) {
                    tile = (start, sizeMeasure.AddOffset(start, size));
                    return true;
                }
            }
        }
        tile = default;
        return false;
    }

    public Range<TPoint> GetSmallestCoveringTile(Range<TPoint> range)
        => TryGetSmallestCoveringTile(range, out var tile)
            ? tile
            : throw new ArgumentOutOfRangeException(nameof(range));

    // Protected methods

    protected abstract TSize[] GetTileSizes();
}
