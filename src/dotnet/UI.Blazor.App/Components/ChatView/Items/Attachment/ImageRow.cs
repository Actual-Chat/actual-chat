namespace ActualChat.UI.Blazor.App.Components;

public class ImageRow
{
    public IReadOnlyList<ImageTile> Tiles { get; }

    public float GetTileWidthInPercent(ImageRow row, ImageTile tile) {
        float widthSum = 0;
        foreach (var item in row.Tiles)
            widthSum += item.WidthPart;
        return widthSum == 0 ? 100 : tile.WidthPart / widthSum * 100;
    }

    private float GetRowHeightRatio(ImageRow row) {
        float ratio = 0;
        foreach (var item in row.Tiles) {
            ratio += item.Proportions switch {
                ImageProportions.Narrow => 8,
                ImageProportions.Square => 4,
                ImageProportions.Wide => 2,
                _ => 1,
            };
        }
        return ratio / row.Tiles.Count;
    }

    private string GetRowHeightInRem(ImageRow row) {
        var heightRatio = GetRowHeightRatio(row);
        return heightRatio switch {
                <= 1 => "line-height-sm",
                <= 3 => "line-height-md",
                <= 7 => "line-height-lg",
                _ => "line-height-xl",
            };
    }

    public string LineHeightCls(ImageRow row) => GetRowHeightInRem(row);

    public ImageRow(IReadOnlyList<ImageTile> tiles)
        => Tiles = tiles;
}

public class ImageTile {
    public TextEntryAttachment Attachment { get; }

    public ImageProportions Proportions { get; }
    public int WidthPart { get; }

    public float Ratio { get; }

    public ImageTile(TextEntryAttachment attachment) {
        Attachment = attachment;
        Ratio = (float)Attachment.Media.Width / Attachment.Media.Height;
        Proportions = Ratio switch {
            <= 0.75f => ImageProportions.Narrow,
            <= 1.25f => ImageProportions.Square,
            <= 2 => ImageProportions.Wide,
            _ => ImageProportions.ExtraWide,
        };
        WidthPart = Ratio switch {
            <= 0.75f => 1,
            <= 1.25f => 2,
            <= 2 => 3,
            _ => 4,
        };
    }
}

public enum ImageProportions {
    Narrow,
    Square,
    Wide,
    ExtraWide,
}
