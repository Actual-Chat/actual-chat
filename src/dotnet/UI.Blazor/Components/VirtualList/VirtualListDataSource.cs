namespace ActualChat.UI.Blazor.Components;

public static class VirtualListDataSource<TItem>
    where TItem : IVirtualListItem
{
    public static IVirtualListDataSource<TItem> Empty { get; } = new EmptyDataSource();

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
