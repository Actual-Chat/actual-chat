using System;
using ActualChat.Mathematics.Internal;
using Stl.Time;

namespace ActualChat.Mathematics
{
    public abstract class SizeMeasure<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public TPoint ZeroPoint { get; init; } = default!;
        public TSize ZeroSize { get; init; } = default!;

        public abstract TSize GetDistance(TPoint start, TPoint end);
        public abstract TPoint AddOffset(TPoint point, TSize offset);

        public abstract TSize Add(TSize first, TSize second);
        public abstract TSize Subtract(TSize first, TSize second);
        public abstract TSize Multiply(TSize size, double multiplier);
        public abstract TSize Modulo(TSize size, TSize modulo);

        public TSize GetDistance(Range<TPoint> range)
            => GetDistance(range.End, range.Start);
        public TPoint SubtractOffset(TPoint point, TSize offset)
            => AddOffset(point, ToNegative(offset));
        public TSize ToNegative(TSize size)
            => Subtract(ZeroSize, size);
    }

    public static class SizeMeasure
    {
        public static SizeMeasure<long, long> Long { get; } = new LongSizeMeasure();
        public static SizeMeasure<double, double> Double { get; } = new DoubleSizeMeasure();
        public static SizeMeasure<Moment, TimeSpan> Moment { get; } = new MomentSizeMeasure();

        public static ConvertingSizeMeasure<TPoint, TSize> New<TPoint, TSize>(
            Func<TPoint, double> pointToDouble,
            Func<double, TPoint> pointFromDouble,
            Func<TSize, double> sizeToDouble,
            Func<double, TSize> sizeFromDouble)
            where TPoint : notnull
            where TSize : notnull
            => new(pointToDouble, pointFromDouble, sizeToDouble, sizeFromDouble);
    }
}
