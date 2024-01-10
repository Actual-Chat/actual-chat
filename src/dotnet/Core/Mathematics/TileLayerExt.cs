namespace ActualChat.Mathematics;

public static class TileLayerExt
{
    public static IEnumerable<Tile<long>> Tiles(this TileLayer<long> idTileLayer, Range<long> idRange)
    {
        for (var idTile = idTileLayer.GetTile(idRange.Start);
             idTile.Start < idRange.End;
             idTile = idTile.Next())
            yield return idTile;
    }
}
