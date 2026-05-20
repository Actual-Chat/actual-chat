using ActualChat.Search;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true, AllowPrivate = true)]
public partial class MentionSearchResult : SearchResult
{
    [DataMember, MemoryPackOrder(2)]
    public Picture Picture { get; }

    [DataMember, MemoryPackOrder(3)]
    public bool IsChatMember { get; init; }

    // Light, non-bold suffix shown after the name in the picker, e.g. "emoji", "place",
    // "in this place", "in Fusion Place", "- not in this chat". Null = no suffix.
    [DataMember, MemoryPackOrder(4)]
    public string? Description { get; init; }

    // Name baked into the mention markup when this result is picked — e.g. "Fusion › Funny Chat"
    // for a cross-place chat, the plain title otherwise.
    [DataMember, MemoryPackOrder(5)]
    public string MentionName { get; init; } = "";

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
