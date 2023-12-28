using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class ImageRow
{
    private readonly float _ratioSum;
    public IReadOnlyList<ImageTile> Tiles { get; }
    public ImageTile Narrowest => Tiles.OrderBy(x => x.Ratio).First();

    public float GetTileWidthPercent(ImageTile tile)
        => tile.Ratio / _ratioSum * 100;

    public float GetTileDesiredWidthRem(ImageTile tile, ScreenSize screenSize)
    {
        var heightRem = GetHeightRem(screenSize);
        return heightRem * tile.Ratio;
    }

    public string HeightCls => $"h-{GetHeightRem(ScreenSize.Small) * 4} md:h-{GetHeightRem(ScreenSize.Medium) * 4}";

    public float GetHeightRem(ScreenSize screenSize)
    {
        var isNarrow = screenSize.IsNarrow();
        return Narrowest.Proportions switch {
                ImageProportions.Narrow => isNarrow ? 64 : 120,
                ImageProportions.Square => isNarrow ? 48 : 80,
                ImageProportions.Wide => isNarrow ? 36 : 60,
                _ => isNarrow ? 24 : 40,
            }
            / 4F;
    }

    public ImageRow(IReadOnlyList<ImageTile> tiles)
    {
        Tiles = tiles;
        _ratioSum = tiles.Sum(x => x.Ratio);
    }
}

public class ImageTile {
    public TextEntryAttachment Attachment { get; }

    public ImageProportions Proportions { get; }

    public float Ratio { get; }

    public int RowQuota => Proportions switch
    {
        ImageProportions.ExtraWide => Constants.Chat.ImageRowCapacity,
        ImageProportions.Wide => Constants.Chat.ImageRowCapacity - 1,
        ImageProportions.Square => Constants.Chat.ImageRowCapacity - 2,
        _ => Constants.Chat.ImageRowCapacity - 3,
    };

    public ImageTile(TextEntryAttachment attachment) {
        Attachment = attachment;
        Ratio = (float)Attachment.Media.Width / Attachment.Media.Height;
        Proportions = Ratio switch {
            <= 0.75f => ImageProportions.Narrow,
            <= 1.25f => ImageProportions.Square,
            <= 2 => ImageProportions.Wide,
            _ => ImageProportions.ExtraWide,
        };
    }
}

public enum ImageProportions {
    Narrow,
    Square,
    Wide,
    ExtraWide,
}
