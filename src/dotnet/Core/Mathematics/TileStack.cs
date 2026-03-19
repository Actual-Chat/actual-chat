using System.Numerics;
using ActualChat.Mathematics.Internal;

namespace ActualChat.Mathematics;

/// <summary>
/// A hierarchical stack of <see cref="TileLayer{T}"/> instances with increasing tile sizes.
/// </summary>
public sealed class TileStack<T>
    where T : struct, INumber<T>
{
    public T Zero { get; }
    public T MinTileSize { get; }
    public T MaxTileSize { get; }
    public ImmutableArray<TileLayer<T>> Layers { get; }
    public TileLayer<T> FirstLayer { get; }
    public TileLayer<T> LastLayer { get; }

    public TileStack(T zero, T tileSize)
        : this(zero, tileSize, Enumerable.Empty<int>())
    { }

    public TileStack(T zero, T minTileSize, int tileSizeMultiplier, int layerCount)
        : this(zero, minTileSize, Enumerable.Range(0, layerCount - 1).Select(_ => tileSizeMultiplier))
    { }

    public TileStack(T zero, T minTileSize, T maxTileSize, int tileSizeMultiplier)
        : this(zero, minTileSize, GetTileSizeMultipliers(minTileSize, maxTileSize, tileSizeMultiplier))
    { }

    public TileStack(T zero, T minTileSize, IEnumerable<int> tileSizeMultipliers)
    {
        Zero = zero;
        MinTileSize = minTileSize;
        var lastLayer = new TileLayer<T>() {
            Stack = this,
            Index = 0,
            Zero = zero,
            TileSize = minTileSize,
            TileSizeMultiplier = 1,
        };
        var layers = new List<TileLayer<T>>() { lastLayer };
        foreach (var tileSizeMultiplier in tileSizeMultipliers) {
            var layer = new TileLayer<T>() {
                Stack = this,
                Index = lastLayer.Index + 1,
                Zero = zero,
                TileSize = lastLayer.TileSize * T.CreateChecked(tileSizeMultiplier),
                TileSizeMultiplier = tileSizeMultiplier,
            };
            layers.Add(layer);
            lastLayer = layer;
        }
        Layers = layers.ToImmutableArray();
        FirstLayer = Layers[0];
        LastLayer = Layers[^1];
        MaxTileSize = LastLayer.TileSize;
    }

    public bool TryGetTile(Range<T> range, out Tile<T> tile)
    {
        foreach (var layer in Layers) {
            if (layer.TryGetTile(range, out tile))
                return true;
        }
        tile = default;
        return false;
    }

    public Tile<T> GetTile(Range<T> range)
        => TryGetTile(range, out var tile)
            ? tile
            : throw Errors.InvalidTileBoundaries(nameof(range));

    public bool IsTile(Range<T> range)
        => TryGetTile(range, out _);

    public void AssertIsTile(Range<T> range)
    {
        if (!TryGetTile(range, out _))
            throw Errors.InvalidTileBoundaries(nameof(range));
    }

    public Tile<T>[] GetAllTiles(T point)
    {
        var result = new Tile<T>[Layers.Length];
        for (var i = 0; i < Layers.Length; i++) {
            var layer = Layers[i];
            result[i] = layer.GetTile(point);
        }
        return result;
    }

    public bool TryGetSmallestCoveringTile(Range<T> range, out Tile<T> tile)
    {
        foreach (var layer in Layers) {
            tile = layer.GetTile(range.Start);
            if (range.End <= tile.End)
                return true;
        }
        tile = default;
        return false;
    }

    public Tile<T>[] GetOptimalCoveringTiles(Range<T> range)
        => LastLayer.GetOptimalCoveringTiles(range);

    // Private methods

    private static IEnumerable<int> GetTileSizeMultipliers(T minTileSize, T maxTileSize, int tileSizeMultiplier)
    {
        var tileSize = minTileSize * T.CreateChecked(tileSizeMultiplier);
        while (tileSize <= maxTileSize) {
            yield return tileSizeMultiplier;
            tileSize *= T.CreateChecked(tileSizeMultiplier);
        }
    }
}
