using System;
using System.Collections.Generic;
using System.Linq;

namespace ActualChat.UI.Blazor
{
    public record VirtualListResponse<TItem>(List<KeyValuePair<string, TItem>> Items)
    {
        public bool HasVeryFirstItem { get; init; }
        public bool HasVeryLastItem { get; init; }

        public VirtualListResponse() : this(new List<KeyValuePair<string, TItem>>()) { }
    }

    public static class VirtualListResponse
    {
        public static VirtualListResponse<TItem> New<TItem>(
            List<KeyValuePair<string, TItem>> items,
            bool hasVeryFirstItem = false,
            bool hasVeryLastItem = false)
            => new(items) {
                HasVeryFirstItem = hasVeryFirstItem,
                HasVeryLastItem = hasVeryLastItem,
            };

        public static VirtualListResponse<TItem> New<TItem>(
            IEnumerable<KeyValuePair<string, TItem>> items,
            bool hasVeryFirstItem = false,
            bool hasVeryLastItem = false)
            => new(items.ToList()) {
                HasVeryFirstItem = hasVeryFirstItem,
                HasVeryLastItem = hasVeryLastItem,
            };

        public static VirtualListResponse<TItem> New<TItem>(
            IEnumerable<TItem> items,
            Func<TItem, string> keySelector,
            bool hasVeryFirstItem = false,
            bool hasVeryLastItem = false)
            => new(items.Select(item => KeyValuePair.Create(keySelector.Invoke(item), item)).ToList()) {
                HasVeryFirstItem = hasVeryFirstItem,
                HasVeryLastItem = hasVeryLastItem,
            };
    }
}
