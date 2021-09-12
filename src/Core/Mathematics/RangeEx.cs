using System;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public static class RangeEx
    {
        public static long Size(this Range<long> range)
            => range.End - range.Start;

        public static double Size(this Range<double> range)
            => range.End - range.Start;

        public static TimeSpan Size(this Range<Moment> range)
            => range.End - range.Start;

        public static TimeSpan Size(this Range<DateTime> range)
            => range.End - range.Start;

        // Geometry-related

        public static Range<double> Move(this Range<double> range, double offset)
            => new(range.Start + offset, range.End + offset);
        public static Range<double> Expand(this Range<double> range, double offset)
            => new(range.Start - offset, range.End + offset);
        public static Range<double> Resize(this Range<double> range, double size)
            => new(range.Start, range.Start + size);

        public static bool Contains(this Range<double> range, double value)
            => range.Start <= value && value < range.End;
        public static bool Contains(this Range<double> range, Range<double> containedRange)
            => range.Start <= containedRange.Start && containedRange.End <= range.End;

        public static bool Overlaps(this Range<double> range, Range<double> otherRange)
            => range.IntersectWith(otherRange).Size() > 0;

        public static Range<double> IntersectWith(this Range<double> range, Range<double> other)
        {
            var result = new Range<double>(Math.Max(range.Start, other.Start), Math.Min(range.End, other.End));
            return result.Size() < 0 ? default : result;
        }
    }
}
