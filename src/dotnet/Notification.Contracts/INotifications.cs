namespace ActualChat.Notification;

public interface INotifications
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<bool> IsSubscribedToChat(Session session, string chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task SubscribeToChat(SubscribeToChatCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task UnsubscribeToChat(UnsubscribeToChatCommand command, CancellationToken cancellationToken);

    public record RegisterDeviceCommand(Session Session, string DeviceId, DeviceType DeviceType) : ISessionCommand;
    public record SubscribeToChatCommand(Session Session, string ChatId) : ISessionCommand;
    public record UnsubscribeToChatCommand(Session Session, string ChatId) : ISessionCommand;
}
