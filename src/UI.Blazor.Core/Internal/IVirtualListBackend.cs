using System.Collections.Generic;

namespace ActualChat.UI.Blazor.Internal
{
    public interface IVirtualListBackend
    {
        public record ClientViewInfo
        {
            public static readonly ClientViewInfo Default = new ClientViewInfo();

            public double TotalHeight { get; set; } = 1_000_000;
            public double VisibleHeight { get; set; } = 1;
            public double ScrollTop { get; set; } = 0;
        }

        public class ClientSideState
        {
            public ClientViewInfo ViewInfo { get; set; } = ClientViewInfo.Default;
            public Dictionary<string, double> ItemHeights { get; set; } = new();
        }

        public void UpdateClientSideState(ClientSideState clientSideState);
    }
}
