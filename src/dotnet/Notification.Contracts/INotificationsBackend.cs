using ActualLab.Rpc;

namespace ActualChat.Notification;

/// <summary>
/// Backend service for managing push notifications and devices.
/// </summary>
public interface INotificationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Notification?> Get(NotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ExplicitNotification?> GetExplicit(ExplicitNotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<Device>> ListDevices(UserId userId, NotificationChannel? channel, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        UserId userId, Moment minSentAt, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnNotify(NotificationsBackend_Notify command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnUpsert(NotificationsBackend_Upsert command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<bool> OnUpsertExplicitNotification(
        NotificationsBackend_UpsertExplicitNotification command,
        CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRegisterDevice(NotificationsBackend_RegisterDevice command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveDevices(NotificationsBackend_RemoveDevices command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(NotificationsBackend_RemoveAccount command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMembers(NotificationsBackend_NotifyMembers command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyMentionedMembers(NotificationsBackend_NotifyMentionedMembers command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEventEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnSignedOut(UserSignedOutEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to send a notification to a user.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Notify(
    [property: DataMember, MemoryPackOrder(0), Key(0)]
    Notification Notification
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

/// <summary>
/// Command to create or update a notification record.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Upsert(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Notification Notification
) : ICommand<bool>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

/// <summary>
/// Command to create or update an explicit (user-created) notification.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_UpsertExplicitNotification(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ExplicitNotification Notification
) : ICommand<bool>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

/// <summary>
/// Command to register a device for push notifications.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RegisterDevice(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] DeviceType DeviceType,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Symbol SessionHash,
    [property: DataMember, MemoryPackOrder(3), Key(3)] NotificationChannel NotificationChannel
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to unregister devices from push notifications.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveDevices(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Symbol[] DeviceIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Symbol> // Review
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Symbol ShardKey => DeviceIds.FirstOrDefault();
}

/// <summary>
/// Command to remove all notification data for a deleted account.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveAccount(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to notify all members of a chat about new entries.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyMembers(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long LastEntryId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to notify users who were mentioned in a message.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyMentionedMembers(
    [property: DataMember, MemoryPackOrder(0), Key(0)] UserId UserId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ChatEntryId ChatEntryId,
    [property: DataMember, MemoryPackOrder(2), Key(2)] UserId[] UserIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId ShardKey => UserId;
}
