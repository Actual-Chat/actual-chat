namespace ActualChat.Notification.Backend;

public interface INotificationsBackend : IComputeService
{
    [ComputeMethod]
    Task<ImmutableArray<Device>> ListDevices(string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListSubscriberIds(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<string>> ListRecentNotificationIds(string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<NotificationEntry> GetNotification(string userId, string notificationId, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task NotifyUser(NotifyUserCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveDevices(RemoveDevicesCommand removeDevicesCommand, CancellationToken cancellationToken);

    [DataContract]
    public sealed record NotifyUserCommand(
        [property: DataMember] string UserId,
        [property: DataMember] NotificationEntry Entry
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record RemoveDevicesCommand(
        [property: DataMember] ImmutableArray<string> DeviceIds
    ) : ICommand<Unit>, IBackendCommand;
}
