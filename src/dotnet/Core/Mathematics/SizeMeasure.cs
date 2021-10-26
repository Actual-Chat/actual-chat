using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

public abstract class SizeMeasure<TPoint, TSize>
    where TPoint : notnull
    where TSize : notnull
{
    public TPoint ZeroPoint { get; init; } = default!;

    public abstract TSize Add(TSize first, TSize second);
    public abstract TPoint AddOffset(TPoint point, TSize offset);

    public abstract TSize GetDistance(TPoint start, TPoint end);

    public TSize GetSize(Range<TPoint> range)
        => GetDistance(range.Start, range.End);

    public bool IsZeroPoint(TPoint point)
        => EqualityComparer<TPoint>.Default.Equals(ZeroPoint, point);

    public abstract TSize Modulo(TSize size, TSize modulo);
    public abstract TSize Multiply(TSize size, double multiplier);
    public abstract TSize Subtract(TSize first, TSize second);

    public TPoint SubtractOffset(TPoint point, TSize offset)
        => AddOffset(point, ToNegative(offset));

    public TSize ToNegative(TSize size)
        => Subtract(default!, size);
}

public static class SizeMeasure
{
    public static SizeMeasure<long, long> Long { get; } = new LongSizeMeasure();
    public static SizeMeasure<double, double> Double { get; } = new DoubleSizeMeasure();
    public static SizeMeasure<Moment, TimeSpan> Moment { get; } = new MomentSizeMeasure();

    public static ConvertingSizeMeasure<TPoint, TSize> New<TPoint, TSize>(
        Func<TPoint, long> pointToLong,
        Func<long, TPoint> pointFromLong,
        Func<TSize, long> sizeToLong,
        Func<long, TSize> sizeFromLong)
        where TPoint : notnull
        where TSize : notnull
        => new (pointToLong, pointFromLong, sizeToLong, sizeFromLong);
}
