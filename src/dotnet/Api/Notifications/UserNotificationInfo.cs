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
    public ApiArray<Notification> Displayed { get; init; }
    [DataMember(Order = 3), Key(3)]
    public NotificationDelta UnsentDelta { get; init; } = NotificationDelta.Empty;
    [DataMember(Order = 4), Key(4)]
    public Moment LastPushAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool IsDormant { get; init; }

    // Upserts notification into Displayed: a notification with the same Id is merged into the
    // existing one (coalescing subtypes accumulate their state), otherwise it's appended.
    public UserNotificationInfo WithNotification(Notification notification)
    {
        var id = notification.Id;
        var existing = Displayed.FirstOrDefault(n => n.Id == id);
        var merged = notification.MergeWith(existing);
        var displayed = existing != null
            ? Displayed.WithUpdate(n => n.Id == id, _ => merged)
            : Displayed.With(merged);
        return this with { Displayed = displayed };
    }
}
