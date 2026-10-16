namespace ActualChat.Chat;

/// <summary>
/// System entry recording that a call participant's hand went up or down.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record HandRaisedEntry(ChatEntryId Id, long Version = 0) : SystemEntry(Id, Version)
{
    [DataMember, Key(20)] public AuthorId? TargetAuthorId { get; init; }
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";
    [DataMember, Key(22)] public bool IsHandRaised { get; init; }

    public HandRaisedEntry() : this(null!, 0)
    { }
}
