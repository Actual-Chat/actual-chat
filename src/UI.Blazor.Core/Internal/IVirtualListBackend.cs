using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ActualChat.UI.Blazor.Internal
{
    public interface IVirtualListBackend
    {
        public class ClientSideState
        {
            public long RenderIndex { get; set; }
            public double ViewportOffset { get; set; } = 0;
            public double ViewportSize { get; set; } = 1;
            [JsonIgnore, Newtonsoft.Json.JsonIgnore]
            public Range<double> Viewport => new(ViewportOffset, ViewportOffset + ViewportSize);
            public Dictionary<string, double> ItemSizes { get; set; } = new();
        }

        public void UpdateClientSideState(ClientSideState clientSideState);
    }

    public class ViewportState { }
}
