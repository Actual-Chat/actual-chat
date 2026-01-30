using ActualChat.Search;

namespace ActualChat.MLSearch;

public static class ContactSearchQueryExt
{
    public static ContactSearchQuery Clamp(this ContactSearchQuery query)
        => query with {
            Skip = query.Skip.Clamp(0, int.MaxValue),
            Limit = query.Limit.Clamp(0, Constants.Search.PageSizeLimit),
        };
}
