namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// Justified layout arranger: packs images into rows so that each row
/// fills the full container width with minimal cropping.
/// Row height is determined by the sum of aspect ratios in the row.
/// </summary>
public class JustifiedGalleryArranger : IVisualMediaGalleryArranger
{
    /// <summary>Target row aspect ratio (width / height). Higher = shorter rows.</summary>
    private readonly float _targetRowRatio;
    /// <summary>Maximum images per row.</summary>
    private readonly int _maxPerRow;

    public JustifiedGalleryArranger(float targetRowRatio = 3.5f, int maxPerRow = 4)
    {
        _targetRowRatio = targetRowRatio;
        _maxPerRow = maxPerRow;
    }

    public VisualMediaGalleryLine<TItem>[] Arrange<TItem>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, (int width, int height)> getSize)
    {
        var tiles = new List<VisualMediaGalleryTile<TItem>>(items.Count);
        foreach (var item in items) {
            var (width, height) = getSize(item);
            tiles.Add(new VisualMediaGalleryTile<TItem>(item, width, height));
        }

        var lines = new List<VisualMediaGalleryLine<TItem>>();
        var currentRow = new List<VisualMediaGalleryTile<TItem>>();
        var currentRatioSum = 0f;

        foreach (var tile in tiles) {
            currentRow.Add(tile);
            currentRatioSum += tile.Ratio;

            // Complete row when ratio sum exceeds target or row is full
            if (currentRatioSum >= _targetRowRatio || currentRow.Count >= _maxPerRow) {
                lines.Add(new VisualMediaGalleryLine<TItem>(currentRow.ToArray()));
                currentRow.Clear();
                currentRatioSum = 0f;
            }
        }

        if (currentRow.Count == 0)
            return lines.ToArray();

        var canMerge = currentRow.Count == 1
            && lines.Count > 0
            && lines[^1].Tiles.Count < _maxPerRow;
        if (canMerge) {
            var merged = lines[^1].Tiles.Concat(currentRow).ToArray();
            lines[^1] = new VisualMediaGalleryLine<TItem>(merged);
        }
        else {
            var isOrphan = currentRow.Count == 1 && lines.Count > 0;
            lines.Add(new VisualMediaGalleryLine<TItem>(currentRow.ToArray(), isOrphan));
        }

        return lines.ToArray();
    }
}
