namespace ActualChat.Users;

/// <summary>
/// One ad or link that brought the user to the app. The user's first one is <see cref="IsInitial"/> and
/// their most recent one <see cref="IsLatest"/>; a single attribution may be both, any other is neither.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserAttribution(
    [property: DataMember, Key(0)] long Id,
    [property: DataMember, Key(1)] UserId UserId,
    [property: DataMember, Key(2)] AttributionOrigin Origin,
    [property: DataMember, Key(3)] AttributionDetails Details,
    [property: DataMember, Key(4)] Moment FirstSeenAt,
    [property: DataMember, Key(5)] Moment LastSeenAt,
    [property: DataMember, Key(6)] bool IsInitial,
    [property: DataMember, Key(7)] bool IsLatest,
    [property: DataMember, Key(8)] string Raw = ""
);
