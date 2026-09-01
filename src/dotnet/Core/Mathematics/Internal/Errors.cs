namespace ActualChat.Mathematics.Internal;

public static class Errors
{
    public static Exception UnboundTile()
        => new InvalidOperationException("The Tile isn't bound to a TileLayer.");

    public static Exception InvalidTileBoundaries(string paramName)
        => new ArgumentOutOfRangeException(paramName, "Invalid tile boundaries.");
}
