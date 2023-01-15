using System.Numerics;

namespace ActualChat;

public static class VectorExt
{
    public static Vector2 ToVector2(this (int X, int Y) source)
        => new(source.X, source.Y);

    public static (int X, int Y) ToIntPair(this Vector2 source)
        => ((int)Math.Round(source.X), (int)Math.Round(source.Y));

    public static Vector2 ScaleToFitInto(this Vector2 source, Vector2 boundary)
    {
        if (source.X <= boundary.X && source.Y <= boundary.Y)
            return source;
        var ratio = Math.Min(boundary.X / source.X, boundary.Y / source.Y);
        return source * ratio;
    }
}
