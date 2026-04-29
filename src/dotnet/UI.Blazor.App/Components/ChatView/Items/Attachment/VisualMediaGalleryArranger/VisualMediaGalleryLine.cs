using System.Globalization;

namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryLine<TItem>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public IReadOnlyList<VisualMediaGalleryTile<TItem>> Tiles { get; }

    public VisualMediaGalleryLine(IReadOnlyList<VisualMediaGalleryTile<TItem>> tiles)
        => Tiles = tiles;

    public string GetTileStyle(VisualMediaGalleryTile<TItem> tile) {
        var ratioSum = GetRatioSum();
        var widthPercent = ratioSum == 0 ? 100 : tile.Ratio / ratioSum * 100;
        return $"width: {widthPercent.ToString(Inv)}%";
    }

    public string GetRowStyle(float maxRowRatio = 5f) {
        var ratioSum = GetRatioSum();
        if (ratioSum == 0)
            return "";

        if (Tiles.Count == 1) {
            var ratio = Tiles[0].Ratio;
            // max-h-96 = 24rem; limit width to preserve aspect ratio
            var maxWidthRem = 24f * Math.Min(ratio, 1.5f);
            return $"aspect-ratio: {ratio.ToString(Inv)}; max-width: {maxWidthRem.ToString(Inv)}rem";
        }

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
