namespace ActualChat.Live;

public enum CallStatus
{
    None = 0,
    Dialing = 1,
    Accepted = 2,
    Declined = 3,
    NoAnswer = 4,
}

/// <summary>
/// The caller-facing status of an outgoing call, kept for a short while past the session itself
/// so the caller can be told how it went. <see cref="CallStatus.None"/> is never stored - it is
/// the absence of this record.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record CallState
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public AuthorId CallerId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public CallStatus Status { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public Moment ChangedAt { get; init; }
}
