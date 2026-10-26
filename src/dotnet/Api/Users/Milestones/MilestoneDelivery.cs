namespace ActualChat.Users;

/// <summary>
/// Where one milestone stands with one <see cref="MilestoneTarget"/>, and the attribution it was credited to.
/// <see cref="Note"/> says why it was skipped or failed.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record MilestoneDelivery(
    [property: DataMember, Key(0)] long MilestoneId,
    [property: DataMember, Key(1)] MilestoneTarget Target,
    [property: DataMember, Key(2)] MilestoneDeliveryState State,
    [property: DataMember, Key(3)] Moment UpdatedAt
)
{
    [DataMember, Key(4)] public long? AttributionId { get; init; }
    [DataMember, Key(5)] public int Attempts { get; init; }
    [DataMember, Key(6)] public Moment? NextAttemptAt { get; init; }
    [DataMember, Key(7)] public string Note { get; init; } = "";
}
