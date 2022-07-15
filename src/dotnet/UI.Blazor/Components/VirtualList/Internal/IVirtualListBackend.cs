// ReSharper disable once CheckNamespace
namespace ActualChat.UI.Blazor.Components.Internal;

public interface IVirtualListBackend
{
    Task RequestData(VirtualListDataQuery query);
}
