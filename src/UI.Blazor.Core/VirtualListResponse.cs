using System.Collections.Generic;
using System.Collections.Immutable;

namespace ActualChat.UI.Blazor
{
    public record VirtualListResponse<TItem>(List<KeyValuePair<string, TItem>> Items)
    {
        public bool StartIsFixed { get; init; } = false;
        public bool EndIsFixed { get; init; } = false;

        public VirtualListResponse() : this(new List<KeyValuePair<string, TItem>>()) { }
    }
}
