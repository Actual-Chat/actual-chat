using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// Per-user notification state: the converged set on the device plus the delta
/// not yet pushed. One small blob per user, owned by the notifications backend.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserNotificationInfo(
    [property: DataMember(Order = 0), Key(0)] UserId UserId,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasVersion<long>
{
    [DataMember(Order = 2), Key(2)]
    public ApiArray<NotificationItem> Displayed { get; init; }
    [DataMember(Order = 3), Key(3)]
    public NotificationDelta UnsentDelta { get; init; } = NotificationDelta.Empty;
    [DataMember(Order = 4), Key(4)]
    public Moment LastPushAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool IsDormant { get; init; }
}
