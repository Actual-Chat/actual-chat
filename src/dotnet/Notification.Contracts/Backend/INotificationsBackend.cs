namespace ActualChat.Notification.Backend;

public interface INotificationsBackend : IComputeService
{
    [ComputeMethod]
    Task<ImmutableArray<Device>> ListDevices(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<NotificationId>> ListRecentNotificationIds(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Notification> Get(NotificationId notificationId, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task Notify(NotifyCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Upsert(UpsertCommand notification, CancellationToken cancellationToken);
    [CommandHandler]
    Task RemoveDevices(RemoveDevicesCommand removeDevicesCommand, CancellationToken cancellationToken);

    [DataContract]
    public sealed record NotifyCommand(
        [property: DataMember] Notification Notification
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record UpsertCommand(
        [property: DataMember] Notification Notification
    ) : ICommand<Unit>, IBackendCommand;

    [DataContract]
    public sealed record RemoveDevicesCommand(
        [property: DataMember] ImmutableArray<Symbol> DeviceIds
    ) : ICommand<Unit>, IBackendCommand;
}
