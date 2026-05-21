namespace ActualChat.Notifications;

/// <summary>
/// Service for managing user notifications and device registrations.
/// </summary>
public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<NotificationItem?> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minSentAt, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<bool> HasNotifiedMentionedMembers(
        Session session, ChatEntryId chatEntryId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnHandle(Notifications_Handle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(Notifications_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDeregisterDevice(Notifications_DeregisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(Notifications_NotifyMembers command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMentionedMembers(Notifications_NotifyMentionedMembers command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_Handle(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] NotificationId NotificationId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_RegisterDevice(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] DeviceType DeviceType
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_DeregisterDevice(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol DeviceId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMembers(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMentionedMembers(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatEntryId ChatEntryId
) : ISessionCommand<Unit>, IApiCommand;
