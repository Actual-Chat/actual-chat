namespace ActualChat.UI.Blazor.App.Components;

public interface IVisualMediaGalleryArranger
{
    VisualMediaGalleryLine<TItem>[] Arrange<TItem>(
        IReadOnlyCollection<TItem> items,
        Func<TItem, (int width, int height)> getSize);
}

public static class VisualMediaGalleryArranger
{
    public static IVisualMediaGalleryArranger Default { get; } = new JustifiedGalleryArranger(targetRowRatio: 3f, maxPerRow: 5);
    public static IVisualMediaGalleryArranger Narrow { get; } = new JustifiedGalleryArranger(targetRowRatio: 2.5f, maxPerRow: 3);
    public static IVisualMediaGalleryArranger ConversationWideScreen { get; } = new JustifiedGalleryArranger(targetRowRatio: 4f, maxPerRow: 6);

    public static IVisualMediaGalleryArranger Pick(bool isConversation, bool isNarrow)
        => isNarrow ? Narrow
            : isConversation ? ConversationWideScreen
            : Default;
}
