
namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryLine<TItem>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public IReadOnlyList<VisualMediaGalleryTile<TItem>> Tiles { get; }
    public bool IsOrphanRow { get; }

    public VisualMediaGalleryLine(IReadOnlyList<VisualMediaGalleryTile<TItem>> tiles, bool isOrphanRow = false)
    {
        Tiles = tiles;
        IsOrphanRow = isOrphanRow;
    }

    public string GetTileStyle(VisualMediaGalleryTile<TItem> tile) {
        var ratioSum = GetRatioSum();
        var widthPercent = ratioSum == 0 ? 100 : tile.Ratio / ratioSum * 100;
        return $"width: {widthPercent.ToString(Inv)}%";
    }

    public string GetRowStyle(int totalItems = 0, float maxRowRatio = 5f) {
        var ratioSum = GetRatioSum();
        if (ratioSum == 0)
            return "";

        if (Tiles.Count == 1 && totalItems <= 1) {
            var ratio = Tiles[0].Ratio;
            // max-h-96 = 24rem; limit width to preserve aspect ratio
            var maxWidthRem = 24f * Math.Min(ratio, 1.5f);
            return $"aspect-ratio: {ratio.ToString(Inv)}; max-width: {maxWidthRem.ToString(Inv)}rem";
        }

        // Orphan row: single item that couldn't merge into previous row.
        // Cap its aspect ratio so it doesn't tower over the gallery.
        if (IsOrphanRow && Tiles.Count == 1) {
            var ratio = Tiles[0].Ratio;
            // Force minimum aspect ratio of 2 (wide) so vertical images don't get too tall
            var clamped = Math.Max(ratio, 2f);
            return $"aspect-ratio: {clamped.ToString(Inv)}";
        }

        // Clamp: don't let a single narrow image make the row too tall
        var clampedSum = Math.Max(ratioSum, 1f);
        // Don't let rows be too short either
        clampedSum = Math.Min(clampedSum, maxRowRatio);
        return $"aspect-ratio: {clampedSum.ToString(Inv)}";
    }

    private float GetRatioSum() {
        var sum = 0f;
        foreach (var tile in Tiles)
            sum += tile.Ratio;
        return sum;
    }
}
