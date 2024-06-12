using MemoryPack;

namespace ActualChat.Notification;

public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Notification?> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minSentAt, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnHandle(Notifications_Handle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(Notifications_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(Notifications_NotifyMembers command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_Handle(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] NotificationId NotificationId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_RegisterDevice(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2)] DeviceType DeviceType
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Notifications_NotifyMembers(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId
) : ISessionCommand<Unit>, IApiCommand;
