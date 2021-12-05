// ReSharper disable once CheckNamespace
namespace ActualChat.UI.Blazor.Controls.Internal;

public interface IVirtualListBackend
{
    Task<long> UpdateClientSideState(VirtualListClientSideState clientSideState);
}
