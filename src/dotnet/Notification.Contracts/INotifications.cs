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

    public record RegisterDeviceCommand(Session Session, string DeviceId, DeviceType DeviceType) : ISessionCommand<bool>;
    public record SubscribeToChatCommand(Session Session, string ChatId) : ISessionCommand<bool>;
    public record UnsubscribeToChatCommand(Session Session, string ChatId) : ISessionCommand<Unit>;
}
