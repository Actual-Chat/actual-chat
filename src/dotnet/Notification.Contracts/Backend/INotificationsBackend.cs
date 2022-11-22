namespace ActualChat.Notification.Backend;

public interface INotificationsBackend : IComputeService
{
    [ComputeMethod]
    Task<ImmutableArray<Device>> ListDevices(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListRecentNotificationIds(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<NotificationEntry> GetNotification(UserId userId, string notificationId, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task NotifyUser(NotifyUserCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveDevices(RemoveDevicesCommand removeDevicesCommand, CancellationToken cancellationToken);

    [DataContract]
    public sealed record NotifyUserCommand(
        [property: DataMember] UserId UserId,
        [property: DataMember] NotificationEntry Entry
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record RemoveDevicesCommand(
        [property: DataMember] ImmutableArray<Symbol> DeviceIds
    ) : ICommand<Unit>, IBackendCommand;
}
