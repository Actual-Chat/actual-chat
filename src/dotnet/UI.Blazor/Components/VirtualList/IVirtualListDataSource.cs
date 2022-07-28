namespace ActualChat.UI.Blazor.Components;

public interface IVirtualListDataSource<TItem>
    where TItem : IVirtualListItem
{
    Task<VirtualListData<TItem>> GetData(
        VirtualListDataQuery query,
        VirtualListData<TItem> oldData,
        CancellationToken cancellationToken);
}
