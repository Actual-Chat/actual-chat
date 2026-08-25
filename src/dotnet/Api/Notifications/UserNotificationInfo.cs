using ActualLab.Versioning;

namespace ActualChat.Notifications;

/// <summary>
/// Per-user notification state: the converged set shown on the user's devices.
/// One small blob per user, owned by the notifications backend.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserNotificationInfo(
    [property: DataMember(Order = 0), Key(0)] UserId UserId,
    [property: DataMember(Order = 1), Key(1)] long Version = 0
    ) : IHasVersion<long>
{
    [DataMember(Order = 2), Key(2)]
    public ApiArray<Notification> Items { get; init; }
    // Key 3 held UnsentDelta; keys are wire format, so it stays vacant rather than being reused.
    [DataMember(Order = 4), Key(4)]
    public Moment LastPushAt { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool IsDormant { get; init; }
    // Dismissals this user is still owed. Appended in the same commit that removes the
    // notification from Items - that atomicity is why this lives in the blob rather than its own
    // table - and cleared only once the dismissal push has actually gone out.
    [DataMember(Order = 6), Key(6)]
    public ApiArray<PendingDismissal> PendingDismissals { get; init; }

    public UserNotificationInfo WithPendingDismissals(IEnumerable<PendingDismissal> dismissals)
    {
        var pending = PendingDismissals;
        foreach (var dismissal in dismissals)
            pending = pending.Without(x => x.Id == dismissal.Id).With(dismissal);
        // Bounded: an unreachable device must not grow the blob without limit. The client-side
        // NotificationReconciler prunes whatever falls off here on its next run.
        var extra = pending.Count - Constants.Notification.MaxPendingDismissals;
        if (extra > 0)
            pending = pending.Skip(extra).ToApiArray();
        return this with { PendingDismissals = pending };
    }

    public UserNotificationInfo WithoutPendingDismissals(IReadOnlyCollection<NotificationId> ids)
        => ids.Count == 0
            ? this
            : this with { PendingDismissals = PendingDismissals.Without(x => ids.Contains(x.Id)) };

    // Upserts notification into Items: a notification with the same Id is merged into the
    // existing one (coalescing subtypes accumulate their state), otherwise it's appended.
    public UserNotificationInfo WithNotification(Notification notification)
    {
        var id = notification.Id;
        var existing = Items.FirstOrDefault(n => n.Id == id);
        var merged = notification.MergeWith(existing);
        var items = existing != null
            ? Items.WithUpdate(n => n.Id == id, _ => merged)
            : Items.With(merged);
        return this with { Items = items };
    }
}

/// <summary>
/// A dismissal the user's devices are still owed. <see cref="Tag"/> is captured at removal time:
/// it derives from the notification, which is gone from
/// <see cref="UserNotificationInfo.Items"/> by the time this is sent.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record PendingDismissal(
    [property: DataMember(Order = 0), Key(0)] NotificationId Id,
    [property: DataMember(Order = 1), Key(1)] string Tag,
    [property: DataMember(Order = 2), Key(2)] Moment QueuedAt);
