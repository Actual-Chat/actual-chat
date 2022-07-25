namespace ActualChat.Notification;

public interface INotifications : IComputeService
{
    [ComputeMethod]
    Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10)]
    Task<ImmutableArray<string>> ListRecentNotificationIds(Session session, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10)]
    Task<NotificationEntry> GetNotification(Session session, string notificationId, CancellationToken cancellationToken);

    [CommandHandler]
    Task HandleNotification(HandleNotificationCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task SetStatus(SetStatusCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record RegisterDeviceCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string DeviceId,
        [property: DataMember] DeviceType DeviceType
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record SetStatusCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId,
        [property: DataMember] bool IsMuted
        ) : ISessionCommand<Unit>;

    [DataContract]
    public sealed record HandleNotificationCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string NotificationId
    ) : ISessionCommand<Unit>;
}
