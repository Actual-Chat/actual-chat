// ReSharper disable once CheckNamespace
namespace ActualChat.UI.Blazor.Components.Internal;

public interface IVirtualListBackend
{
    // Task<long> UpdateClientSideState(VirtualListClientSideState clientSideState);
    Task RequestNewData(VirtualListDataQuery query);
}
