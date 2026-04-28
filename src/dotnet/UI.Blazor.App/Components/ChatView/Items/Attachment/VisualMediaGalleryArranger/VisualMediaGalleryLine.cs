using System.Globalization;

namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryLine<TItem>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public IReadOnlyList<VisualMediaGalleryTile<TItem>> Tiles { get; }

    public VisualMediaGalleryLine(IReadOnlyList<VisualMediaGalleryTile<TItem>> tiles)
        => Tiles = tiles;

    /// <summary>
    /// Tile width as percentage of row width, proportional to its aspect ratio.
    /// In justified layout: wider images get more space, all share the same height.
    /// </summary>
    public float GetTileWidthInPercent(VisualMediaGalleryTile<TItem> tile) {
        var ratioSum = GetRatioSum();
        return ratioSum == 0 ? 100 : tile.Ratio / ratioSum * 100;
    }

    /// <summary>
    /// Row aspect-ratio style. The row ratio = sum of tile ratios,
    /// so all tiles at the same height fill exactly 100% width.
    /// </summary>
    public string GetRowStyle(float maxRowRatio = 5f) {
        var ratioSum = GetRatioSum();
        if (ratioSum == 0)
            return "";
        // Clamp: don't let a single narrow image make the row too tall
        var clamped = Math.Max(ratioSum, 1f);
        // Don't let rows be too short either
        clamped = Math.Min(clamped, maxRowRatio);
        return $"aspect-ratio: {clamped.ToString(Inv)}";
    }

    private float GetRatioSum() {
        var sum = 0f;
        foreach (var tile in Tiles)
            sum += tile.Ratio;
        return sum;
    }
}
