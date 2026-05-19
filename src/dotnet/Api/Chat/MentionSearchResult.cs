using ActualChat.Search;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true, AllowPrivate = true)]
public partial class MentionSearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public Picture Picture { get; }

    [DataMember, MemoryPackOrder(3)]
    public bool IsChatMember { get; init; }

    // Non-null when the candidate is in a different place than the picker's host
    // chat (or when there's no host place). The picker uses it for a "| PlaceTitle"
    // suffix on the displayed name.
    [DataMember, MemoryPackOrder(4)]
    public string? PlaceTitleSuffix { get; init; }

    // True when the candidate's chat lives in the same place as the picker's host chat.
    // Distinguishes "this place's chat" from generic "chat" in the picker description.
    [DataMember, MemoryPackOrder(5)]
    public bool IsInHostPlace { get; init; }

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
