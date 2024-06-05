using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Notification;

public interface INotificationsBackend : IComputeService, IBackendService
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
    Task<bool> OnUpsert(NotificationsBackend_Upsert command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(NotificationsBackend_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveDevices(NotificationsBackend_RemoveDevices command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(NotificationsBackend_RemoveAccount command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(NotificationsBackend_NotifyMembers command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Notify(
    [property: DataMember, MemoryPackOrder(0)]
    Notification Notification
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Notification.UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0)] Notification Notification
) : ICommand<bool>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => Notification.UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RegisterDevice(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2)] DeviceType DeviceType
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveDevices(
    [property: DataMember, MemoryPackOrder(0)] ApiArray<Symbol> DeviceIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Symbol> // Review
{
    [IgnoreDataMember, MemoryPackIgnore]
    public Symbol ShardKey => !DeviceIds.IsEmpty ? DeviceIds[0] : default;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveAccount(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyMembers(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] long LastEntryId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserId ShardKey => UserId;
}
