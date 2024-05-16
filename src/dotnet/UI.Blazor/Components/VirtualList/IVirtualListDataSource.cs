namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListDataSource<TItem>
    where TItem : class, IVirtualListItem
{
    Task<VirtualListData<TItem>> GetData(
        ComputedState<VirtualListData<TItem>> state,
        VirtualListDataQuery query,
        VirtualListData<TItem> renderedData,
        CancellationToken cancellationToken);
}
