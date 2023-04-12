using ActualChat.Search;

namespace ActualChat.Chat;

[DataContract]
public class MentionSearchResult : SearchResult
{
    [DataMember] public string Picture { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore] public MentionId MentionId => new (Id);
    public MentionSearchResult(MentionId id, SearchMatch searchMatch, string picture = "")
        : base(id, searchMatch)
        => Picture = picture;
}
