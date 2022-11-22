namespace ActualChat.Notification;

public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<ImmutableArray<Symbol>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10)]
    Task<NotificationEntry> GetNotification(Session session, Symbol notificationId, CancellationToken cancellationToken);

    [CommandHandler]
    Task HandleNotification(HandleNotificationCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record RegisterDeviceCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol DeviceId,
        [property: DataMember] DeviceType DeviceType
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record HandleNotificationCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol NotificationId
    ) : ISessionCommand<Unit>;
}
