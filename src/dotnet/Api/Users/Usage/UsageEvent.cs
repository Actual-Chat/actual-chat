using ActualChat.Live;

namespace ActualChat.Users;

/// <summary>
/// One recorded unit of a user's activity: what happened, when, where it came from, and how much.
/// <see cref="Value"/> is milliseconds for <see cref="UsageEventKind.Speech"/> and
/// <see cref="UsageEventKind.LiveSession"/>, a count otherwise (+1/-1 for <see cref="UsageEventKind.Contact"/>).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UsageEvent(
    [property: DataMember, Key(0)] UsageEventKind Kind,
    [property: DataMember, Key(1)] Moment OccurredAt,
    [property: DataMember, Key(2)] string SourceId,
    [property: DataMember, Key(3)] long Value,
    [property: DataMember, Key(4)] UsageEventAttributes? Attributes = null
);

/// <summary>
/// The fixed attribute set a <see cref="UsageEvent"/> may carry; every member is optional.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UsageEventAttributes
{
    [DataMember, Key(0)] public ChatKind? ChatKind { get; init; }
    [DataMember, Key(1)] public LiveSessionKind? SessionKind { get; init; }
    [DataMember, Key(2)] public int? ParticipantCount { get; init; }
    [DataMember, Key(3)] public bool? IsViaApi { get; init; }
}
