using System;

namespace ActualChat.Mathematics
{
    public sealed class ConvertingSizeMeasure<TPoint, TSize> : ISizeMeasure<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public Func<TPoint, double> PointToDouble { get; }
        public Func<double, TPoint> PointFromDouble { get; }
        public Func<TSize, double> SizeToDouble { get; }
        public Func<double, TSize> SizeFromDouble { get; }

        public ConvertingSizeMeasure(
            Func<TPoint, double> pointToDouble,
            Func<double, TPoint> pointFromDouble,
            Func<TSize, double> sizeToDouble,
            Func<double, TSize> sizeFromDouble)
        {
            PointToDouble = pointToDouble;
            PointFromDouble = pointFromDouble;
            SizeToDouble = sizeToDouble;
            SizeFromDouble = sizeFromDouble;
        }

        public TSize GetDistance(TPoint start, TPoint end)
            => SizeFromDouble(PointToDouble(end) - PointToDouble(start));
        public TPoint AddOffset(TPoint point, TSize offset)
            => PointFromDouble(PointToDouble(point) + SizeToDouble(offset));

        public TSize Add(TSize first, TSize second)
            => SizeFromDouble(SizeToDouble(first) + SizeToDouble(second));
        public TSize Subtract(TSize first, TSize second)
            => SizeFromDouble(SizeToDouble(first) - SizeToDouble(second));
        public TSize Multiply(TSize size, double multiplier)
            => SizeFromDouble(SizeToDouble(size) * multiplier);
        public TSize Modulo(TSize size, TSize modulo)
            => SizeFromDouble(SizeToDouble(size) % SizeToDouble(modulo));
    }
}
