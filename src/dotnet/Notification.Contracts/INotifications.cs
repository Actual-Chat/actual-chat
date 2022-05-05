namespace ActualChat.Notification;

public interface INotifications
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<bool> IsSubscribedToChat(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<bool> RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<bool> SubscribeToChat(SubscribeToChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task UnsubscribeToChat(UnsubscribeToChatCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record RegisterDeviceCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string DeviceId,
        [property: DataMember] DeviceType DeviceType
        ) : ISessionCommand<bool>;

    [DataContract]
    public sealed record SubscribeToChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<bool>;

    [DataContract]
    public sealed record UnsubscribeToChatCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ChatId
        ) : ISessionCommand<Unit>;
}
