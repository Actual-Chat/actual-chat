namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaGalleryTile<TItem>
{
    public TItem Item { get; }
    public float Ratio { get; }

    public VisualMediaGalleryTile(TItem item, int width, int height)
    {
        Item = item;
        Ratio = height > 0 ? (float)width / height : 1f;
    }
}
