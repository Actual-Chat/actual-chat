namespace ActualChat.Users;

/// <summary>
/// What Redis holds per app kind. <see cref="LastSeenStoreVersion"/> and
/// <see cref="LastSeenReleasedAt"/> are the baseline the train-only App Store path
/// compares against; they say nothing about what's published.
/// <see cref="PreviousInfo"/> is what <see cref="Info"/> replaced, and it's what
/// clients are told while a freshly detected release waits out the announce delay.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AppUpdateRecord(
    [property: DataMember, Key(0)] AppUpdateInfo? Info,
    [property: DataMember, Key(1)] string LastSeenStoreVersion,
    [property: DataMember, Key(2)] Moment LastSeenReleasedAt,
    [property: DataMember, Key(3)] Moment ProbedAt,
    [property: DataMember, Key(4)] AppUpdateInfo? PreviousInfo = null);
