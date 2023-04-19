using ActualChat.Search;
using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
public class MentionSearchResult : SearchResult
{
    [DataMember] public UserPicture Picture { get; }
    [IgnoreDataMember] public MentionId MentionId => new (Id);
    public MentionSearchResult(MentionId id, SearchMatch searchMatch, UserPicture picture)
        : base(id, searchMatch)
        => Picture = picture;
}
