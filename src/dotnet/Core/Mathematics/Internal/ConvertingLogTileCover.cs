namespace ActualChat.Mathematics.Internal;

public class ConvertingLogTileCover<TPoint, TSize> : LogTileCover<TPoint, TSize>
    where TPoint : notnull
    where TSize : notnull
{
    public LogTileCover<long, long> BaseTiles { get; }
    public ConvertingSizeMeasure<TPoint, TSize> ConvertingMeasure { get; }

    public ConvertingLogTileCover(
        LogTileCover<long, long> baseTiles,
        ConvertingSizeMeasure<TPoint, TSize> convertingMeasure)
    {
        BaseTiles = baseTiles;
        ConvertingMeasure = convertingMeasure;
        Measure = convertingMeasure;
        Zero = ConvertingMeasure.PointFromLong(BaseTiles.Zero);
        MinTileSize = ConvertingMeasure.SizeFromLong(BaseTiles.MinTileSize);
        MaxTileSize = ConvertingMeasure.SizeFromLong(BaseTiles.MaxTileSize);
    }

    protected override TSize[] GetTileSizes()
        => BaseTiles.TileSizes.Select(s => ConvertingMeasure.SizeFromLong(s)).ToArray();
}
