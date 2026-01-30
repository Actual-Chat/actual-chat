using ActualChat.Search;

namespace ActualChat.MLSearch;

public static class EntrySearchQueryExt
{
    public static EntrySearchQuery Clamp(this EntrySearchQuery query)
        => query with {
            Skip = query.Skip.Clamp(0, int.MaxValue),
            Limit = query.Limit.Clamp(0, Constants.Search.PageSizeLimit),
        };
}
