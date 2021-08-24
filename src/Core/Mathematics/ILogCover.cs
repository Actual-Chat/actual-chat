using System;
using System.Collections.Generic;
using Stl;

namespace ActualChat.Mathematics
{
    public interface ILogCover<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public TPoint Zero { get; }
        public TSize MinRangeSize { get; }
        public TSize MaxRangeSize { get; }
        public int RangeSizeFactor { get; }
        public TSize[] RangeSizes { get; }
        public ISizeMeasure<TPoint, TSize> SizeMeasure { get; }

        public void AssertValidRange(Range<TPoint> range)
        {
            if (!IsValidRange(range))
                throw new NotSupportedException("Invalid range boundaries.");
        }

        public bool IsValidRange(Range<TPoint> range)
        {
            var sizeMeasure = SizeMeasure;
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

        public TPoint GetRangeStart(TPoint innerPoint, int rangeSizeIndex)
        {
            var sizeMeasure = SizeMeasure;
            var size = RangeSizes[rangeSizeIndex];
            var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
            return sizeMeasure.SubtractOffset(innerPoint, offset);
        }

        public IEnumerable<Range<TPoint>> GetRanges(TPoint innerPoint)
        {
            var sizeMeasure = SizeMeasure;
            foreach (var size in RangeSizes) {
                var offset = sizeMeasure.Modulo(sizeMeasure.GetDistance(Zero, innerPoint), size);
                var start = sizeMeasure.SubtractOffset(innerPoint, offset);
                yield return new Range<TPoint>(start, sizeMeasure.AddOffset(start, size));
            }
        }

        public Option<Range<TPoint>> TryGetRange(Range<TPoint> innerRange)
        {
            var sizeMeasure = SizeMeasure;
            var comparer = Comparer<TSize>.Default;

            var minSize = sizeMeasure.GetDistance(innerRange);
            if (comparer.Compare(sizeMeasure.ZeroSize, minSize) > 0)
                throw new ArgumentOutOfRangeException(nameof(innerRange));

            // TODO(AY): Use binary search when (RangeSizes.Length >= 8) or so
            for (var i = 0; i < RangeSizes.Length; i++) {
                var size = RangeSizes[i];
                if (comparer.Compare(size, minSize) > 0) {
                    var start = GetRangeStart(innerRange.Start, i);
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
    }
}
