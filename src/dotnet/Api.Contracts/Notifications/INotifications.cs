namespace ActualChat.Notifications;

/// <summary>
/// Service for managing user notifications and device registrations.
/// </summary>
public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 30)]
    Task<ApiArray<Notification>> ListActive(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> HasNotifiedMentionedMembers(
        Session session, ChatEntryId chatEntryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnDismiss(Notifications_Dismiss command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDismissAll(Notifications_DismissAll command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(Notifications_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeregisterDevice(Notifications_DeregisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(Notifications_NotifyMembers command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMentionedMembers(Notifications_NotifyMentionedMembers command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_Dismiss : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required NotificationId NotificationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_DismissAll : ApiCommand<Unit>;

// Not deduplicated: registering a device token is an idempotent upsert the client re-sends to
// refresh a stale record, so suppressing a repeat is worse than running it.

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_RegisterDevice : ApiCommand<Unit>, INotDeduplicated
{
    [DataMember(Order = 2), Key(2)] public required Symbol DeviceId { get; init; }
    [DataMember(Order = 3), Key(3)] public required DeviceType DeviceType { get; init; }
    // Not required, and false by default: PTT is per-device opt-in, and a command from a client
    // predating the flag must not enroll that device.
    [DataMember(Order = 4), Key(4)] public bool IsPttEnabled { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_DeregisterDevice : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required Symbol DeviceId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMembers : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatId ChatId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMentionedMembers : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ChatEntryId ChatEntryId { get; init; }
}
