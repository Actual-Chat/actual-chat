namespace ActualChat.Chat;

/// <summary>
/// System entry emitted when a member joins or leaves the chat.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record MembersChangedEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId? TargetAuthorId { get; init; }
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";
    [DataMember, Key(22)] public bool HasLeft { get; init; }

    public MembersChangedEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public MembersChangedEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
