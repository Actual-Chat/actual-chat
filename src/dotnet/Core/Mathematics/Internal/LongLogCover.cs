namespace ActualChat.Mathematics.Internal;

public sealed class LongLogCover : LogCover<long, long>
{
    public LongLogCover()
    {
        MinTileSize = 16;
        MaxTileSize = 16_384;
        Measure = SizeMeasure.Long;
    }

    protected override long[] GetTileSizes()
        => Enumerable.Range(0, int.MaxValue)
            .Select(i => MinTileSize * (long) Math.Pow(TileSizeFactor, i))
            .TakeWhile(size => size <= MaxTileSize)
            .ToArray();
}
