namespace ActualChat.Mathematics.Internal;

public sealed class ConvertingSizeMeasure<TPoint, TSize> : SizeMeasure<TPoint, TSize>
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

    public override TSize GetDistance(TPoint start, TPoint end)
        => SizeFromDouble(PointToDouble(end) - PointToDouble(start));
    public override TPoint AddOffset(TPoint point, TSize offset)
        => PointFromDouble(PointToDouble(point) + SizeToDouble(offset));

    public override TSize Add(TSize first, TSize second)
        => SizeFromDouble(SizeToDouble(first) + SizeToDouble(second));
    public override TSize Subtract(TSize first, TSize second)
        => SizeFromDouble(SizeToDouble(first) - SizeToDouble(second));
    public override TSize Multiply(TSize size, double multiplier)
        => SizeFromDouble(SizeToDouble(size) * multiplier);

    public override TSize Modulo(TSize size, TSize modulo)
    {
        var doubleModulo = SizeToDouble(modulo);
        var result = SizeToDouble(size) % doubleModulo;
        if (result < 0)
            result += doubleModulo;
        return SizeFromDouble(result);
    }
}
