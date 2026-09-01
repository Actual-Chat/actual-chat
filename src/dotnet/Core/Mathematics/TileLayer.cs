using System.Numerics;
using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

/// <summary>
/// A layer of uniformly-sized tiles: aligned ranges of <see cref="TileSize"/>
/// items, all offset from <see cref="Zero"/>.
/// </summary>
public sealed class TileLayer<T>(T zero, T tileSize)
    where T : struct, INumber<T>
{
    public T Zero { get; } = zero;
    public T TileSize { get; } = tileSize;

    public bool TryGetTile(Range<T> range, out Tile<T> tile)
    {
        var size = range.End - range.Start;
        if (EqualityComparer<T>.Default.Equals(size, TileSize)) {
            var mod = TileMath.Mod(range.Start - Zero, TileSize);
            if (EqualityComparer<T>.Default.Equals(mod, default)) {
                tile = new(range, this);
                return true;
            }
        }
        tile = default;
        return false;
    }

    public Tile<T> GetTile(Range<T> range)
        => TryGetTile(range, out var tile)
            ? tile
            : throw Errors.InvalidTileBoundaries(nameof(range));

    public Tile<T> GetTile(T point)
    {
        var offset = point - Zero;
        var tileIndex = TileMath.DivNonNegativeRem(offset, TileSize, out _);
        var start = TileSize * T.CreateChecked(tileIndex) + Zero;
        var end = start + TileSize;
        return new(start, end, this);
    }

    public bool IsTile(Range<T> range)
        => TryGetTile(range, out _);

    public Tile<T>[] GetCoveringTiles(Range<T> range)
    {
        if (range.IsEmptyOrNegative)
            return [];

        var tiles = ArrayBuffer<Tile<T>>.Lease(true);
        try {
            var tile = GetTile(range.Start);
            tiles.Add(tile);
            while (tile.End < range.End) {
                tile = tile.Next();
                tiles.Add(tile);
            }
            return tiles.ToArray();
        }
        finally {
            tiles.Release();
        }
    }
}
