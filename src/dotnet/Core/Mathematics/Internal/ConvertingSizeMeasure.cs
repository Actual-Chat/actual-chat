namespace ActualChat.Mathematics.Internal;

public sealed class ConvertingSizeMeasure<TPoint, TSize> : SizeMeasure<TPoint, TSize>
    where TPoint : notnull
    where TSize : notnull
{
    public Func<TPoint, long> PointToLong { get; }
    public Func<long, TPoint> PointFromLong { get; }
    public Func<TSize, long> SizeToLong { get; }
    public Func<long, TSize> SizeFromLong { get; }

    public ConvertingSizeMeasure(
        Func<TPoint, long> pointToLong,
        Func<long, TPoint> pointFromLong,
        Func<TSize, long> sizeToLong,
        Func<long, TSize> sizeFromLong)
    {
        PointToLong = pointToLong;
        PointFromLong = pointFromLong;
        SizeToLong = sizeToLong;
        SizeFromLong = sizeFromLong;
    }

    public override TSize Add(TSize first, TSize second)
        => SizeFromLong(SizeToLong(first) + SizeToLong(second));

    public override TPoint AddOffset(TPoint point, TSize offset)
        => PointFromLong(PointToLong(point) + SizeToLong(offset));

    public override TSize GetDistance(TPoint start, TPoint end)
        => SizeFromLong(PointToLong(end) - PointToLong(start));

    public override TSize Modulo(TSize size, TSize modulo)
    {
        var doubleModulo = SizeToLong(modulo);
        var result = SizeToLong(size) % doubleModulo;
        if (result < 0)
            result += doubleModulo;
        return SizeFromLong(result);
    }

    public override TSize Multiply(TSize size, double multiplier)
        => SizeFromLong((long)(SizeToLong(size) * multiplier));

    public override TSize Subtract(TSize first, TSize second)
        => SizeFromLong(SizeToLong(first) - SizeToLong(second));
}
