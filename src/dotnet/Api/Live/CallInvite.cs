namespace ActualChat.Live;

public enum CallInviteStatus
{
    New = 0,
    Ringing = 1,
    Accepted = 2,
    Active = 3,
    Declined = 4,
    Missed = 5,
    Ended = 6,
    Busy = 7,
}

[DataContract, MessagePackObject]
public sealed partial record CallInvite
{
    [DataMember(Order = 0), Key(0)]
    public AuthorId InviteeId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public CallInviteStatus Status { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment RingingAt { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment? RespondedAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment? ActiveAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public Moment? EndedAt { get; init; }
    [DataMember(Order = 6), Key(6)]
    public RingAck? Ack { get; init; }
    [DataMember(Order = 7), Key(7)]
    public Moment? AckAt { get; init; }
}
