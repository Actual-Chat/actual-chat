using System;
using System.Collections.Generic;
using ActualChat.Mathematics.Internal;
using Stl;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public abstract class LogCover<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        private TSize[]? _rangeSizes;

        public TPoint Zero { get; init; } = default!;
        public TSize MinRangeSize { get; init; } = default!;
        public TSize MaxRangeSize { get; init; } = default!;
        public int RangeSizeFactor { get; init; } = 4;
        public SizeMeasure<TPoint, TSize> Measure { get; init; } = null!;
        public TSize[] RangeSizes => _rangeSizes ??= GetRangeSizes();

        public void AssertValidRange(Range<TPoint> range)
        {
            if (!IsValidRange(range))
                throw new NotSupportedException("Invalid range boundaries.");
        }

        public virtual bool IsValidRange(Range<TPoint> range)
        {
            var sizeMeasure = Measure;
            var size = sizeMeasure.GetDistance(range);
            for (var i = 0; i < RangeSizes.Length; i++) {
                var rangeSize = RangeSizes[i];
                if (EqualityComparer<TSize>.Default.Equals(rangeSize, size)) {
                    var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, range.Start), size);
                    return EqualityComparer<TSize>.Default.Equals(offset, sizeMeasure.ZeroSize);
                }
            }
            return false;
        }

        public virtual TPoint GetRangeStart(TPoint innerPoint, int rangeSizeIndex)
        {
            var sizeMeasure = Measure;
            var size = RangeSizes[rangeSizeIndex];
            var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
            return sizeMeasure.SubtractOffset(innerPoint, offset);
        }

        public virtual IEnumerable<Range<TPoint>> GetRanges(TPoint innerPoint)
        {
            var sizeMeasure = Measure;
            foreach (var size in RangeSizes) {
                var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
                var start = sizeMeasure.SubtractOffset(innerPoint, offset);
                yield return new Range<TPoint>(start, sizeMeasure.AddOffset(start, size));
            }
        }

        public virtual Option<Range<TPoint>> TryGetRange(Range<TPoint> innerRange)
        {
            var sizeMeasure = Measure;
            var comparer = Comparer<TSize>.Default;

            var minSize = sizeMeasure.GetDistance(innerRange);
            if (comparer.Compare(sizeMeasure.ZeroSize, minSize) > 0)
                throw new ArgumentOutOfRangeException(nameof(innerRange));

            // TODO(AY): Use binary search when (RangeSizes.Length >= 8) or so
            for (var i = 0; i < RangeSizes.Length; i++) {
                var size = RangeSizes[i];
                if (comparer.Compare(size, minSize) > 0) {
                    var start = GetRangeStart(innerRange.Start, i);
                    // ~ if (start + size >= innerRange.End) ...
                    if (comparer.Compare(size, sizeMeasure.GetDistance(start, innerRange.End)) >= 0)
                        return Option.Some<Range<TPoint>>((start, sizeMeasure.AddOffset(start, size)));
                }
            }
            return default;
        }

        public Range<TPoint> GetRange(Range<TPoint> innerRange)
            => TryGetRange(innerRange).IsSome(out var value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(innerRange));

        // Protected methods

        protected abstract TSize[] GetRangeSizes();
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
}
