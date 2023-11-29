namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListDataSource<TItem>
    where TItem : class, IVirtualListItem
{
    Task<VirtualListData<TItem>> GetData(
        IComputedState<VirtualListData<TItem>> state,
        VirtualListDataQuery query,
        VirtualListData<TItem> renderedData,
        CancellationToken cancellationToken);
}
