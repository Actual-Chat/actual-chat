using System.Collections.Generic;
using System.Collections.Immutable;

namespace ActualChat.UI.Blazor
{
    public record VirtualListResponse<TItem>(List<KeyValuePair<string, TItem>> Items)
    {
        public bool HasVeryFirstItem { get; init; }
        public bool HasVeryLastItem { get; init; }

        public VirtualListResponse() : this(new List<KeyValuePair<string, TItem>>()) { }
    }
}
