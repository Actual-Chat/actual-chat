namespace ActualChat.Live;

public enum CallInviteStatus
{
    Ringing = 0,
    Accepted = 1,
    Declined = 2,
    Missed = 3,
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record CallInvite
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public AuthorId InviteeId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public CallInviteStatus Status { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public Moment RingingAt { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public Moment? RespondedAt { get; init; }
}
