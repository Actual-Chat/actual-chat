using System;

namespace ActualChat.Mathematics
{
    public interface ISizeMeasure<TPoint, TSize>
        where TPoint : notnull
        where TSize : notnull
    {
        public TPoint ZeroPoint => default!;
        public TSize ZeroSize => default!;

        public TSize GetDistance(TPoint start, TPoint end);
        public TSize GetDistance(Range<TPoint> range) => GetDistance(range.End, range.Start);
        public TPoint AddOffset(TPoint point, TSize offset);
        public TPoint SubtractOffset(TPoint point, TSize offset) => AddOffset(point, ToNegative(offset));

        public TSize Add(TSize first, TSize second);
        public TSize Subtract(TSize first, TSize second);
        public TSize Multiply(TSize size, double multiplier);
        public TSize Modulo(TSize size, TSize modulo);

        public TSize ToNegative(TSize size) => Subtract(ZeroSize, size);
    }
}
