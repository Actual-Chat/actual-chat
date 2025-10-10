namespace ActualChat.UI.Blazor.App.Components;

public interface IVisualMediaGalleryArranger
{
    VisualMediaGalleryLine<TItem>[] Arrange<TItem>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, (int width, int height)> getSize);
}

public static class VisualMediaGalleryArranger
{
    public static IVisualMediaGalleryArranger Default { get; } = new DefaultVisualMediaGalleryArranger();
    public static IVisualMediaGalleryArranger ConversationWideScreen { get; } = new ConversationWideScreenVisualMediaGalleryArranger();
}
