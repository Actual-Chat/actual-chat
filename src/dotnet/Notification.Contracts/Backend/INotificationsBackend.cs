using MemoryPack;

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
        UserId userId, Moment minSentAt, CancellationToken cancellationToken);

    // Command handlers

    [CommandHandler]
    Task OnNotify(NotificationsBackend_Notify command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnUpsert(NotificationsBackend_Upsert notification, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveDevices(NotificationsBackend_RemoveDevices removeDevicesCommand, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Notify(
    [property: DataMember, MemoryPackOrder(0)] Notification Notification
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] Notification Notification
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveDevices(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<Symbol> DeviceIds
) : ICommand<Unit>, IBackendCommand;
