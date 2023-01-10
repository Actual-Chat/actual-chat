// ReSharper disable once CheckNamespace
namespace ActualChat.UI.Blazor.Components.Internal;

public interface IVirtualListBackend
{
    [JSInvokable]
    Task RequestData(VirtualListDataQuery query);

    [JSInvokable]
    Task UpdateVisibleKeys(List<string> visibleKeys, bool isIsEndAnchorVisible);
}
