namespace ActualChat.UI.Blazor.App.Components;

public interface IMediaGalleryArranger
{
    MediaGalleryLine<TItem>[] Arrange<TItem>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, (int width, int height)> getSize);
}

public static class MediaGalleryArranger
{
    public static IMediaGalleryArranger Default { get; } = new DefaultMediaGalleryArranger();
    public static IMediaGalleryArranger ConversationWideScreen { get; } = new ConversationWideScreenMediaGalleryArranger();
}

public class DefaultMediaGalleryArranger : IMediaGalleryArranger
{
    public MediaGalleryLine<TItem>[] Arrange<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, (int width, int height)> getSize)
    {
        var resultBuilder = new ArrangeMediaGalleryResultBuilder<TItem>();
        var index = 0;
        foreach (var item in items) {
            var (width, height) = getSize(item);
            var tile = new MediaGalleryTile<TItem>(item, width, height);
            var isLast = index == items.Count - 1;
            var lineIsFull = resultBuilder.Line.Count >= 3;
            var tileIsWide = tile.Proportions is ImageProportions.Wide or ImageProportions.ExtraWide;
            var lineHasManyWide = resultBuilder.Line.Count(t => t.Proportions is ImageProportions.Wide or ImageProportions.ExtraWide) > 1;

            if (items.Count == 2
                && index == 1
                && resultBuilder.Line is [{ Proportions: ImageProportions.Wide or ImageProportions.ExtraWide }]
                && tileIsWide) {
                resultBuilder.CompleteLine();
            }

            if ((!isLast && lineIsFull)
                || (isLast && resultBuilder.Line.Count == 3 && tileIsWide && lineHasManyWide)) {
                resultBuilder.CompleteLine();
            }

            resultBuilder.AddTile(tile);
            index++;
        }
        resultBuilder.CompleteLine();
        return resultBuilder.GetLines();
    }
}

public class ConversationWideScreenMediaGalleryArranger : IMediaGalleryArranger
{
    public MediaGalleryLine<TItem>[] Arrange<TItem>(IReadOnlyCollection<TItem> items, Func<TItem, (int width, int height)> getSize)
    {
        var resultBuilder = new ArrangeMediaGalleryResultBuilder<TItem>();
        var index = 0;
        foreach (var item in items) {
            var (width, height) = getSize(item);
            var tile = new MediaGalleryTile<TItem>(item, width, height);
            var isLast = index == items.Count - 1;
            var lineIsFull = resultBuilder.Line.Count >= 4;
            var tileIsWide = tile.Proportions is ImageProportions.Wide or ImageProportions.ExtraWide;
            var lineHasManyWide = resultBuilder.Line.Count(t => t.Proportions is ImageProportions.Wide or ImageProportions.ExtraWide) > 2;

            if (items.Count == 3
                && index == 1
                && resultBuilder.Line is [{ Proportions: ImageProportions.Wide or ImageProportions.ExtraWide }]
                && tileIsWide) {
                resultBuilder.CompleteLine();
            }

            if ((!isLast && lineIsFull)
                || (isLast && resultBuilder.Line.Count == 4 && tileIsWide && lineHasManyWide)) {
                resultBuilder.CompleteLine();
            }

            resultBuilder.AddTile(tile);
            index++;
        }
        resultBuilder.CompleteLine();
        return resultBuilder.GetLines();
    }
}

internal class ArrangeMediaGalleryResultBuilder<TItem>
{
    private readonly List<MediaGalleryLine<TItem>> _lines = new();
    private readonly List<MediaGalleryTile<TItem>> _line = new();

    public void AddTile(MediaGalleryTile<TItem> tile)
        => _line.Add(tile);

    public IReadOnlyList<MediaGalleryTile<TItem>> Line => _line;

    public void CompleteLine()
    {
        if (_line.Count == 0)
            return;

        _lines.Add(new MediaGalleryLine<TItem>(_line.ToArray()));
        _line.Clear();
    }

    public MediaGalleryLine<TItem>[] GetLines()
        => _lines.ToArray();
}

public class MediaGalleryTile<TItem>
{
    public TItem Item { get; }

    public ImageProportions Proportions { get; }
    public int WidthPart { get; }
    public float Ratio { get; }

    public MediaGalleryTile(TItem item, int width, int height)
    {
        Item = item;
        Ratio = (float)width / height;
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

public class MediaGalleryLine<TItem>
{
    public IReadOnlyList<MediaGalleryTile<TItem>> Tiles { get; }

    public float GetTileWidthInPercent(MediaGalleryTile<TItem> tile) {
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
                ImageProportions.Narrow => 8,
                ImageProportions.Square => 4,
                ImageProportions.Wide => 2,
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

    public MediaGalleryLine(IReadOnlyList<MediaGalleryTile<TItem>> tiles)
        => Tiles = tiles;

    public string GetRowClass() {
        var tiles = Tiles;
        if (tiles.Count > 2)
            return "normal-line";
        if (tiles.Count == 2) {
            if (tiles.Count(t => t.Proportions is ImageProportions.Square) == 2)
                return "md-md";

            if (tiles.Count(t => t.Proportions is ImageProportions.Narrow) == 2)
                return "sm-sm";

            if (tiles.Count(t => t.Proportions is ImageProportions.Narrow) == 1
                && tiles.Count(t => t.Proportions is ImageProportions.Square) == 1)
                return "sm-md";
        }
        if (tiles.Count == 1) {
            var tile = tiles[0];
            return tile.Proportions switch {
                ImageProportions.Narrow => "sm-sm",
                ImageProportions.Square => "md-md",
                ImageProportions.Wide => "lg-lg",
                ImageProportions.ExtraWide => "xl-xl",
                _ => "normal-line",
            };
        }
        return "normal-line";
    }
}

public enum ImageProportions {
    Narrow,
    Square,
    Wide,
    ExtraWide,
}

public enum ImageSize {
    ExtraLarge,
    Large,
    Medium,
    Small,
    Mixed,
}
