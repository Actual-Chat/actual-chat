namespace ActualChat.Chat;

/// <summary>
/// System entry emitted when an author asks for the attention of all members.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record NotifyMembersEntry(ChatEntryId Id, long Version = 0) : SystemEntry(Id, Version)
{
    [DataMember, Key(20)] public AuthorId? TargetAuthorId { get; init; }
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";

    public NotifyMembersEntry() : this(null!, 0) { }
}
