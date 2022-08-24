using ActualChat.Search;

namespace ActualChat.Chat;

public class MentionSearchResult : SearchResult
{
    public MentionSearchResult(string id, SearchMatch searchMatch)
        : base(id, searchMatch) { }
}
