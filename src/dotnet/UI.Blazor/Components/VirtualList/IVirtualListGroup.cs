namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListGroup<out TItem> : IVirtualListItem
    where TItem: class, IVirtualListItem
{
    bool IVirtualListItem.IsGroup => true;
    IReadOnlyList<TItem> Items { get; }
}
