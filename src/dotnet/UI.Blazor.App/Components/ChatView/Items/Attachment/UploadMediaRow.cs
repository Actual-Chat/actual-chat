namespace ActualChat.UI.Blazor.App.Components;

public class UploadMediaRow
{
    public IReadOnlyList<UploadMediaTile> Tiles { get; }

    public float GetTileWidthInPercent(UploadMediaRow row, UploadMediaTile tile) {
        float widthSum = 0;
        foreach (var item in row.Tiles)
            widthSum += item.WidthPart;
        return widthSum == 0 ? 100 : tile.WidthPart / widthSum * 100;
    }

    private float GetRowHeightRatio(UploadMediaRow row) {
        float ratio = 0;
        foreach (var item in row.Tiles) {
            ratio += item.Proportions switch {
                UploadMediaProportions.Narrow => 8,
                UploadMediaProportions.Square => 4,
                UploadMediaProportions.Wide => 2,
                _ => 1,
            };
        }
        return ratio / row.Tiles.Count;
    }

    private string GetRowHeightInRem(UploadMediaRow row) {
        var heightRatio = GetRowHeightRatio(row);
        return heightRatio switch {
                <= 1 => "line-height-sm",
                <= 3 => "line-height-md",
                <= 7 => "line-height-lg",
                _ => "line-height-xl",
            };
    }

    public string LineHeightCls(UploadMediaRow row) => GetRowHeightInRem(row);

    public UploadMediaRow(IReadOnlyList<UploadMediaTile> tiles)
        => Tiles = tiles;
}

public class UploadMediaTile {
    public Attachment Attachment { get; }

    public UploadMediaProportions Proportions { get; }
    public int WidthPart { get; }

    public float Ratio { get; }

    public UploadMediaTile(Attachment attachment) {
        Attachment = attachment;
        if (Attachment.Height == null || Attachment.Width == null || Attachment.Height == 0 || Attachment.Width == 0)
            return;

        Ratio = (float)Attachment.Width / (float)Attachment.Height;
        Proportions = Ratio switch {
            <= 0.75f => UploadMediaProportions.Narrow,
            <= 1.25f => UploadMediaProportions.Square,
            <= 2 => UploadMediaProportions.Wide,
            _ => UploadMediaProportions.ExtraWide,
        };
        WidthPart = Ratio switch {
            <= 0.75f => 1,
            <= 1.25f => 2,
            <= 2 => 3,
            _ => 4,
        };
    }
}

public enum UploadMediaProportions {
    Narrow,
    Square,
    Wide,
    ExtraWide,
}
