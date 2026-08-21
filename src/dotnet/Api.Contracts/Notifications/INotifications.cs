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
    Task OnHandle(Notifications_Handle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnHandleAll(Notifications_HandleAll command, CancellationToken cancellationToken);
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
public sealed partial record Notifications_Handle : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required NotificationId NotificationId { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_HandleAll : ApiCommand<Unit>;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_RegisterDevice : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required Symbol DeviceId { get; init; }
    [DataMember(Order = 3), Key(3)] public required DeviceType DeviceType { get; init; }
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
