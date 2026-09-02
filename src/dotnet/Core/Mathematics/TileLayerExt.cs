using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

public static class TileLayerExt
{
    public static Tile<long> AssertIsTileStart(this TileLayer<long> tiles, long tileStart)
    {
        var tile = tiles.GetTile(tileStart);
        if (tile.Start != tileStart)
            throw Errors.InvalidTileBoundaries(nameof(tileStart));

        return tile;
    }
}
