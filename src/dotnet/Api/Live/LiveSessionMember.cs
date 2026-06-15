namespace ActualChat.Live;

public enum MemberGroup
{
    Host = 0,
    Other = 1,
    Exited = 2,
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LiveSessionMember
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public AuthorId AuthorId { get; init; } = null!;
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public MemberGroup Group { get; init; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public bool IsMicOpen { get; init; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public bool HasCamera { get; init; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public bool HasScreenShare { get; init; }
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public bool IsListening { get; init; }
    [DataMember(Order = 6), MemoryPackOrder(6), Key(6)]
    public bool MicMuted { get; init; }
    [DataMember(Order = 7), MemoryPackOrder(7), Key(7)]
    public bool ForcedMuted { get; init; }
    [DataMember(Order = 8), MemoryPackOrder(8), Key(8)]
    public Moment JoinedAt { get; init; }
}
