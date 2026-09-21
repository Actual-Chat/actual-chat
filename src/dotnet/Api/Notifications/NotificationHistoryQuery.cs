namespace ActualChat.Notifications;

/// <summary>
/// A page request over a user's notification log. <see cref="AfterSeq"/> is a cursor in walk
/// order: with <see cref="IsNewestFirst"/> the page continues to older rows, otherwise to newer.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record NotificationHistoryQuery
{
    // Empty = every logged kind
    [DataMember(Order = 0), Key(0)] public ApiArray<NotificationKind> Kinds { get; init; }
    // 0 = from the start of the walk
    [DataMember(Order = 1), Key(1)] public long AfterSeq { get; init; }
    // 0 = Constants.Notification.HistoryDefaultLimit; capped at HistoryMaxLimit
    [DataMember(Order = 2), Key(2)] public int Limit { get; init; }
    [DataMember(Order = 3), Key(3)] public bool IsNewestFirst { get; init; }
}
