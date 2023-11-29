namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListDataSource<TItem>
    where TItem : class, IVirtualListItem
{
    Task<VirtualListData<TItem>> GetData(
        VirtualListDataQuery query,
        VirtualListData<TItem> oldData,
        CancellationToken cancellationToken);
}
