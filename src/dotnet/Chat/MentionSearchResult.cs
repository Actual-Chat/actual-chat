using ActualChat.Search;

namespace ActualChat.Chat;

[DataContract]
public class MentionSearchResult : SearchResult
{
    [DataMember] public string Picture { get; }

    public MentionSearchResult(string id, SearchMatch searchMatch, string picture = "")
        : base(id, searchMatch)
        => Picture = picture;
}
