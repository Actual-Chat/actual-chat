using System.Text.Json.Serialization;

namespace ActualChat.UI.Blazor.Internal;

public interface IVirtualListBackend
{
    public class ClientSideState
    {
        public long RenderIndex { get; set; }
        public bool IsScrollStopped { get; set; }
        public double ViewOffset { get; set; } = 0; // Minus spacer size!
        public double ViewSize { get; set; } = 1;
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public Range<double> ViewRange => new(ViewOffset, ViewOffset + ViewSize);
        public Dictionary<string, double> ItemSizes { get; set; } = new();
    }

    public void UpdateClientSideState(ClientSideState clientSideState);
}
