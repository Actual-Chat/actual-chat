using ActualChat.Search;
using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MentionSearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public UserPicture Picture { get; }

    [IgnoreDataMember, MemoryPackIgnore]
    public MentionId MentionId => new (Id);

    [MemoryPackConstructor]
    private MentionSearchResult(string id, SearchMatch searchMatch, UserPicture picture)
        : base(id, searchMatch)
        => Picture = picture;

    public MentionSearchResult(MentionId id, SearchMatch searchMatch, UserPicture picture)
        : base(id, searchMatch)
        => Picture = picture;
}
