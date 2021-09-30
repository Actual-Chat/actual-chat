using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

public abstract class LogCover<TPoint, TSize>
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

    public void AssertIsTile(Range<TPoint> range)
    {
        if (!IsTile(range))
            throw new NotSupportedException("Invalid range boundaries.");
    }

    public virtual bool IsTile(Range<TPoint> range)
    {
        var sizeMeasure = Measure;
        var size = sizeMeasure.GetSize(range);
        for (var i = 0; i < TileSizes.Length; i++) {
            var rangeSize = TileSizes[i];
            if (EqualityComparer<TSize>.Default.Equals(rangeSize, size)) {
                var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, range.Start), size);
                return EqualityComparer<TSize>.Default.Equals(offset, default);
            }
        }
        return false;
    }

    public virtual TPoint GetTileStart(TPoint innerPoint, int rangeSizeIndex)
    {
        var sizeMeasure = Measure;
        var size = TileSizes[rangeSizeIndex];
        var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
        return sizeMeasure.SubtractOffset(innerPoint, offset);
    }

    public virtual TPoint GetTileEnd(TPoint innerPoint, int rangeSizeIndex)
    {
        var sizeMeasure = Measure;
        var size = TileSizes[rangeSizeIndex];
        var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
        if (EqualityComparer<TSize>.Default.Equals(offset, default))
            return innerPoint;
        return sizeMeasure.AddOffset(sizeMeasure.SubtractOffset(innerPoint, offset), size);
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

    public virtual IEnumerable<Range<TPoint>> GetCoveringTiles(TPoint innerPoint)
    {
        var sizeMeasure = Measure;
        foreach (var size in TileSizes) {
            var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
            var start = sizeMeasure.SubtractOffset(innerPoint, offset);
            yield return new Range<TPoint>(start, sizeMeasure.AddOffset(start, size));
        }
    }

    public virtual Option<Range<TPoint>> TryGetMinCoveringTile(Range<TPoint> range)
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
                if (comparer.Compare(size, sizeMeasure.GetDistance(start, range.End)) >= 0)
                    return Option.Some<Range<TPoint>>((start, sizeMeasure.AddOffset(start, size)));
            }
        }
        return default;
    }

    public Range<TPoint> GetMinCoveringTile(Range<TPoint> range)
        => TryGetMinCoveringTile(range).IsSome(out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(range));

    // Protected methods

    protected abstract TSize[] GetTileSizes();
}

public static class LogCover
{
    public static class Default
    {
        public static LogCover<long, long> Long { get; } = new LongLogCover();
        public static LogCover<double, double> Double { get; } = new DoubleLogCover();
        public static LogCover<Moment, TimeSpan> Moment { get; } = new MomentLogCover();
    }

    public static ConvertingLogCover<TPoint, TSize> New<TPoint, TSize>(
        DoubleLogCover baseCover,
        ConvertingSizeMeasure<TPoint, TSize> sizeMeasure)
        where TPoint : notnull
        where TSize : notnull
        => new(baseCover, sizeMeasure);
}
