namespace ActualChat.UI.Blazor.Components.Internal;

public interface IVirtualListBackend
{
    /// <summary> The data which is transfered  from js to blazor</summary>
    public class ClientSideState
    {
        public long RenderIndex { get; set; }

        /// <summary> Is Blazor side can call scroll programmly at the moment or not </summary>
        public bool IsSafeToScroll { get; set; }

        /// <summary> Size of div.spacer </summary>
        public double SpacerSize { get; set; }

        /// <summary> Is used to implement sticky top/bottom </summary>
        public double ScrollTop { get; set; }

        /// <summary> Is used to implement sticky bottom </summary>
        public double ScrollHeight { get; set; }

        /// <summary> Height of div.virtual-list </summary>
        public double Height { get; set; }

        public Dictionary<string, double> ItemSizes { get; set; } = new(StringComparer.Ordinal);
    }

    public void UpdateClientSideState(ClientSideState clientSideState);
}
