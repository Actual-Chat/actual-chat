namespace ActualChat.UI.Blazor.Components;

public static class VirtualListDataSource<TItem>
    where TItem : IVirtualListItem
{
    public static readonly IVirtualListDataSource<TItem> Empty = new EmptyDataSource();

    // Nested types

    private class EmptyDataSource : IVirtualListDataSource<TItem>
    {
        public Task<VirtualListData<TItem>> GetData(
            VirtualListDataQuery query,
            VirtualListData<TItem> oldData,
            CancellationToken cancellationToken)
            => Task.FromResult(VirtualListData<TItem>.None);
    }
}
