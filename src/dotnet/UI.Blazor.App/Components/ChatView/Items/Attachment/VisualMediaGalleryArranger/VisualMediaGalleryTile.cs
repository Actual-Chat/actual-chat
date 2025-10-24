namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryTile<TItem>
{
    public TItem Item { get; }

    public VisualMediaProportions Proportions { get; }
    public int WidthPart { get; }
    public float Ratio { get; }

    public VisualMediaGalleryTile(TItem item, int width, int height)
    {
        Item = item;
        Ratio = (float)width / height;
        Proportions = Ratio switch {
            <= 0.75f => VisualMediaProportions.Narrow,
            <= 1.25f => VisualMediaProportions.Square,
            <= 2 => VisualMediaProportions.Wide,
            _ => VisualMediaProportions.ExtraWide,
        };
        WidthPart = Ratio switch {
            <= 0.75f => 1,
            <= 1.25f => 2,
            <= 2 => 3,
            _ => 4,
        };
    }
}
