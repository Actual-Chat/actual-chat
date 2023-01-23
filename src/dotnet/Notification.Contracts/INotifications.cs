namespace ActualChat.Notification;

public interface INotifications : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10)]
    Task<Notification?> Get(Session session, NotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10)]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        Session session, Moment minVersion, CancellationToken cancellationToken);

    [CommandHandler]
    Task Handle(HandleCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record RegisterDeviceCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol DeviceId,
        [property: DataMember] DeviceType DeviceType
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record HandleCommand(
        [property: DataMember] Session Session,
        [property: DataMember] NotificationId NotificationId
    ) : ISessionCommand<Unit>;
}
