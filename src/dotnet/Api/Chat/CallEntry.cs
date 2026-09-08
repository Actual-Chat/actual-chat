namespace ActualChat.Chat;

public enum CallOutcome
{
    None = 0,
    NoAnswer = 1,
    Declined = 2,
    Canceled = 3,
    Ended = 4,
}

/// <summary>
/// System entry recording how a call went: it never connected (<see cref="CallOutcome.NoAnswer"/>,
/// <see cref="CallOutcome.Declined"/>, <see cref="CallOutcome.Canceled"/>) or it connected and
/// finished (<see cref="CallOutcome.Ended"/>).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CallEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId CallerId { get; init; } = null!;
    [DataMember, Key(21)] public string CallerName { get; init; } = "";
    [DataMember, Key(22)] public CallOutcome Outcome { get; init; }
    [DataMember, Key(23)] public ApiArray<AuthorId> InviteeIds { get; init; }
    [DataMember, Key(24)] public bool HasVideo { get; init; }

    public CallEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public CallEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
