using ActualChat.Search;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MentionSearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public Picture Picture { get; }

    [IgnoreDataMember, MemoryPackIgnore]
    public MentionId MentionId => new (Id);

    [MemoryPackConstructor]
    private MentionSearchResult(string id, SearchMatch searchMatch, Picture picture)
        : base(id, searchMatch)
        => Picture = picture;

    public MentionSearchResult(MentionId id, SearchMatch searchMatch, Picture picture)
        : base(id, searchMatch)
        => Picture = picture;
}
