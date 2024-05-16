namespace ActualChat.UI.Blazor.Components;

public static class VirtualListDataSource<TItem>
    where TItem : class, IVirtualListItem
{
    public static readonly IVirtualListDataSource<TItem> Empty = new EmptyDataSource();

    // Nested types

    private class EmptyDataSource : IVirtualListDataSource<TItem>
    {
        public Task<VirtualListData<TItem>> GetData(
            ComputedState<VirtualListData<TItem>> state,
            VirtualListDataQuery query,
            VirtualListData<TItem> renderedData,
            CancellationToken cancellationToken)
            => Task.FromResult(VirtualListData<TItem>.None);
    }
}
