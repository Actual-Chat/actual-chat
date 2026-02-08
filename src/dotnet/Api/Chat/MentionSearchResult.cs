using ActualChat.Search;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class MentionSearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public Picture Picture { get; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public MentionId MentionId => field ??= MentionId.Parse(Id);

    public MentionSearchResult(MentionId id, SearchMatch searchMatch, Picture picture)
        : base(id.Value, searchMatch)
        => Picture = picture;

    [MemoryPackConstructor, SerializationConstructor]
    private MentionSearchResult(string id, SearchMatch searchMatch, Picture picture)
        : base(id, searchMatch)
        => Picture = picture;
}
