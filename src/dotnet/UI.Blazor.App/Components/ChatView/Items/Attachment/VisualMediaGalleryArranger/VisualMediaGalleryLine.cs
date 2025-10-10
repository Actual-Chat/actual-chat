namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryLine<TItem>
{
    public IReadOnlyList<VisualMediaGalleryTile<TItem>> Tiles { get; }

    public float GetTileWidthInPercent(VisualMediaGalleryTile<TItem> tile) {
        if (!Tiles.Contains(tile))
            throw new ArgumentException("Given tile does not belong to this row.", nameof(tile));

        float widthSum = 0;
        foreach (var item in Tiles)
            widthSum += item.WidthPart;
        return widthSum == 0 ? 100 : tile.WidthPart / widthSum * 100;
    }

    public string LineHeightCls() => GetRowHeightInRem();

    private float GetRowHeightRatio() {
        float ratio = 0;
        foreach (var item in Tiles) {
            ratio += item.Proportions switch {
                VisualMediaProportions.Narrow => 8,
                VisualMediaProportions.Square => 4,
                VisualMediaProportions.Wide => 2,
                _ => 1,
            };
        }
        return ratio / Tiles.Count;
    }

    private string GetRowHeightInRem() {
        var heightRatio = GetRowHeightRatio();
        return heightRatio switch {
            <= 1 => "line-height-sm",
            <= 3 => "line-height-md",
            <= 7 => "line-height-lg",
            _ => "line-height-xl",
        };
    }

    public VisualMediaGalleryLine(IReadOnlyList<VisualMediaGalleryTile<TItem>> tiles)
        => Tiles = tiles;

    public string GetRowClass() {
        var tiles = Tiles;
        if (tiles.Count > 2)
            return "normal-line";
        if (tiles.Count == 2) {
            if (tiles.Count(t => t.Proportions is VisualMediaProportions.Square) == 2)
                return "md-md";

            if (tiles.Count(t => t.Proportions is VisualMediaProportions.Narrow) == 2)
                return "sm-sm";

            if (tiles.Count(t => t.Proportions is VisualMediaProportions.Narrow) == 1
                && tiles.Count(t => t.Proportions is VisualMediaProportions.Square) == 1)
                return "sm-md";
        }
        if (tiles.Count == 1) {
            var tile = tiles[0];
            return tile.Proportions switch {
                VisualMediaProportions.Narrow => "sm-sm",
                VisualMediaProportions.Square => "md-md",
                VisualMediaProportions.Wide => "lg-lg",
                VisualMediaProportions.ExtraWide => "xl-xl",
                _ => "normal-line",
            };
        }
        return "normal-line";
    }
}
