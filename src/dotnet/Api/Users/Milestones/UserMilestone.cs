namespace ActualChat.Users;

/// <summary>
/// A <see cref="MilestoneKind"/> the user reached, with the attribution that was latest at that moment.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserMilestone(
    [property: DataMember, Key(0)] long Id,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] MilestoneKind Kind,
    [property: DataMember, Key(3)] Moment ReachedAt,
    [property: DataMember, Key(4)] long? AttributionId = null
);
