namespace ActualChat.Notification;

public interface INotifications : IComputeService
{
    [ComputeMethod]
    Task<ChatNotificationStatus> GetStatus(Session session, string chatId, CancellationToken cancellationToken);

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
}
