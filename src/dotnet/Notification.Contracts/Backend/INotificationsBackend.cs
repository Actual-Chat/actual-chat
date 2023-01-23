namespace ActualChat.Notification.Backend;

public interface INotificationsBackend : IComputeService
{
    [ComputeMethod]
    Task<Notification?> Get(NotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        UserId userId, Moment minVersion, CancellationToken cancellationToken);

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
