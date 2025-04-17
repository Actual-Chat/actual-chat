namespace ActualChat.UI.Blazor.Components;


public interface IVirtualListGroup : IVirtualListItem
{
    bool IVirtualListItem.IsGroup => true;
    int Count { get; }
}

public interface IVirtualListGroup<out TItem> : IVirtualListGroup
    where TItem: class, IVirtualListItem
{
    IReadOnlyList<TItem> Items { get; }
    int IVirtualListGroup.Count => Items.Count;
}
