using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Internal
{
    public interface IVirtualScrollBackend
    {
        public record ClientSideState
        {
            public double ClientHeight { get; init; }
            public double ScrollTop { get; init; }
        }

        public void UpdateClientSideState(ClientSideState clientSideState);
    }
}
