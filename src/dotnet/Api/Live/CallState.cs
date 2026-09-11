namespace ActualChat.Live;

public enum CallStatus
{
    None = 0,
    Dialing = 1,
    Connecting = 2,
    Active = 3,
    NoAnswer = 4,
    Declined = 5,
    Canceled = 6,
    Ended = 7,
}

/// <summary>
/// The caller-facing status of an outgoing call, kept for a short while past the session itself
/// so the caller can be told how it went. <see cref="CallStatus.None"/> is never stored - it is
/// the absence of this record.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CallState
{
    [DataMember(Order = 0), Key(0)]
    public AuthorId CallerId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public CallStatus Status { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment ChangedAt { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment? CallerActiveAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment? CallerEndedAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public Moment? CanceledAt { get; init; }
}
