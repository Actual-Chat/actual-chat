using ActualLab.Rpc;

namespace ActualChat.Notifications;

/// <summary>
/// Backend service for managing push notifications and devices.
/// </summary>
public interface INotificationsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ExplicitNotification?> GetExplicit(ExplicitNotificationId notificationId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<IReadOnlyList<UserId>> ListSubscribedUserIds(
        ChatId chatId, NotificationImportance importance, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<UserNotificationInfo> GetUserNotificationInfo(UserId userId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnNotify(NotificationsBackend_Notify command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnProcess(NotificationsBackend_Process command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnHandle(NotificationsBackend_Handle command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnHandleAll(NotificationsBackend_HandleAll command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPush(NotificationsBackend_Push command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPushDismissal(NotificationsBackend_PushDismissal command, CancellationToken cancellationToken);
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
    [CommandHandler]
    Task OnNotifyConversation(NotificationsBackend_NotifyConversation command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnNotifyCall(NotificationsBackend_NotifyCall command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnCancelCall(NotificationsBackend_CancelCall command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatChangedEventEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnConversationChangedEvent(ConversationChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnReadPositionChangedEvent(ReadPositionChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnSignedOut(UserSignedOutEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Command to send a notification to a user.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Notify(
    [property: DataMember, Key(0)] Notification Notification
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

// Drains a user's in-memory soft-update buffer and applies one coalesced hard update.
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Process(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

// Dismisses a single notification (the user handled it) and pushes a silent badge update.
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Handle(
    [property: DataMember, Key(0)] NotificationId NotificationId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => NotificationId.UserId;
}

// Dismisses every active notification for a user (bulk "mark all read") and pushes a silent
// dismissal.
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_HandleAll(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

// Delivers a notification to the user's devices. Enqueued (not called in-process) so NATS
// retries the FCM send on transient failure and it survives a push-side crash. The badge is not
// carried — OnPush recomputes it from current state at delivery (avoids stale/out-of-order badges).
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_Push(
    [property: DataMember, Key(0)] Notification Notification,
    [property: DataMember, Key(1)] bool IsSilent = false
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

// Pushes a silent dismissal to the user's devices: closes the given banners (tags fully gone) and
// refreshes the badge (recomputed at delivery by OnPushDismissal).
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_PushDismissal(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ApiArray<Notification> Dismissed
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to create or update an explicit (user-created) notification.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_UpsertExplicitNotification(
    [property: DataMember, Key(0)] ExplicitNotification Notification
) : ICommand<bool>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => Notification.UserId;
}

/// <summary>
/// Command to register a device for push notifications.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RegisterDevice(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] Symbol DeviceId,
    [property: DataMember, Key(2)] DeviceType DeviceType,
    [property: DataMember, Key(3)] Symbol SessionHash
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to unregister devices from push notifications.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveDevices(
    [property: DataMember, Key(0)] Symbol[] DeviceIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<Symbol> // Review
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Symbol ShardKey => DeviceIds.FirstOrDefault();
}

/// <summary>
/// Command to remove all notification data for a deleted account.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_RemoveAccount(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to notify all members of a chat about new entries.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyMembers(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] long LastEntryId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// A point in a conversation's lifecycle that a <see cref="ConversationNotification"/> reports.
/// Live conversations go Started → Titled → Final; regular ones emit a single Created.
/// </summary>
public enum ConversationNotificationPhase
{
    Started = 0,
    Titled,
    Created,
    Final,
}

/// <summary>
/// Command to notify a chat's subscribers (minus the conversation's authors, and for live
/// phases minus current participants) about a conversation lifecycle change.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyConversation(
    [property: DataMember, Key(0)] ConversationId ConversationId,
    [property: DataMember, Key(1)] ConversationNotificationPhase Phase,
    [property: DataMember, Key(2)] string Text,
    [property: DataMember, Key(3)] long EndEntryLid,
    [property: DataMember, Key(4)] IReadOnlyList<AuthorId> AuthorIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ConversationId.ChatId;
}

/// <summary>
/// Command to ring the invitees of a voice/video call with an incoming-call notification.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyCall(
    [property: DataMember, Key(0)] ConversationId ConversationId,
    [property: DataMember, Key(1)] AuthorId Caller,
    [property: DataMember, Key(2)] IReadOnlyList<AuthorId> Invitees,
    [property: DataMember, Key(3)] bool HasVideo
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ConversationId.ChatId;
}

/// <summary>
/// Command to dismiss a call's ring on the invitees' devices (cancel/decline/answer/timeout).
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_CancelCall(
    [property: DataMember, Key(0)] ConversationId ConversationId,
    [property: DataMember, Key(1)] IReadOnlyList<AuthorId> Invitees
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ConversationId.ChatId;
}

/// <summary>
/// Command to notify users who were mentioned in a message.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record NotificationsBackend_NotifyMentionedMembers(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ChatEntryId ChatEntryId,
    [property: DataMember, Key(2)] UserId[] UserIds
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
