namespace ActualChat.Mathematics.Internal;

public class ConvertingLogTileCover<TPoint, TSize> : LogTileCover<TPoint, TSize>
    where TPoint : notnull
    where TSize : notnull
{
    public LogTileCover<double, double> BaseTiles { get; }
    public ConvertingSizeMeasure<TPoint, TSize> ConvertingMeasure { get; }

    public ConvertingLogTileCover(
        LogTileCover<double, double> baseTiles,
        ConvertingSizeMeasure<TPoint, TSize> convertingMeasure)
    {
        BaseTiles = baseTiles;
        ConvertingMeasure = convertingMeasure;
        Measure = convertingMeasure;
        Zero = ConvertingMeasure.PointFromDouble(BaseTiles.Zero);
        MinTileSize = ConvertingMeasure.SizeFromDouble(BaseTiles.MinTileSize);
        MaxTileSize = ConvertingMeasure.SizeFromDouble(BaseTiles.MaxTileSize);
    }

    protected override TSize[] GetTileSizes()
        => BaseTiles.TileSizes.Select(s => ConvertingMeasure.SizeFromDouble(s)).ToArray();
}
