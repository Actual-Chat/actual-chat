namespace ActualChat.Notification;

public interface INotifications
{
    [CommandHandler]
    Task RegisterDevice(RegisterDeviceCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task SubscribeToChat(SubscribeToChatCommand command, CancellationToken cancellationToken);

    public record RegisterDeviceCommand(Session Session, string DeviceId, DeviceType DeviceType) : ISessionCommand;
    public record SubscribeToChatCommand(Session Session, string ChatId) : ISessionCommand;
}
