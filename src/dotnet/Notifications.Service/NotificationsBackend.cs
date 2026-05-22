using System.Collections.Concurrent;
using ActualChat.Contacts;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Notifications.Db;
using ActualChat.Notifications.Flows;
using ActualChat.Queues;
using ActualChat.Sharding;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ActualChat.Notifications;

/// <summary>
/// Backend service implementation for managing push notifications and device tokens.
/// </summary>
#pragma warning disable CA1001 // Has disposable _recentChatsWithNotifications
public class NotificationsBackend(IServiceProvider services)
    : ShardedDbServiceBase<NotificationDbContext>(services), INotificationsBackend
#pragma warning restore CA1001
{
    private readonly MemoryCache _recentChatsWithNotifications = new(new MemoryCacheOptions {
        CompactionPercentage = 0.1,
        SizeLimit = 10_000,
        ExpirationScanFrequency = TimeSpan.FromSeconds(5),
    });

    // Per-user soft-update buffers, owned by this shard. Entries are lost on restart by design
    // (see docs/plans/notif-api.md); a committed hard update always re-reads from the DB.
    private readonly ConcurrentDictionary<UserId, SoftBuffer> _softBuffers = new();

    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IChatThreadsBackend ChatThreadsBackend { get; } = services.GetRequiredService<IChatThreadsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private IDbEntityResolver<string, DbNotification> DbNotificationResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbNotification>>();
    private IDbEntityResolver<string, DbExplicitNotification> DbExplicitNotificationResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExplicitNotification>>();

    private IUserPresences UserPresences { get; } = services.GetRequiredService<IUserPresences>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private IFirebaseMessagingClient FirebaseMessagingClient { get; }
        = services.GetRequiredService<IFirebaseMessagingClient>();
    private IQueues Queues { get; } = services.Queues();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private ILogger? DebugLog => Log;

    // [ComputeMethod]
    public virtual async Task<Notification?> Get(
        NotificationId notificationId,
        CancellationToken cancellationToken)
    {
        var dbNotification = await DbNotificationResolver.Get(notificationId.Value, cancellationToken).ConfigureAwait(false);
        return dbNotification?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ExplicitNotification?> GetExplicit(ExplicitNotificationId notificationId, CancellationToken cancellationToken)
    {
        var dbNotification = await DbExplicitNotificationResolver.Get(notificationId.Value, cancellationToken).ConfigureAwait(false);
        return dbNotification?.ToModel();
    }

    // [ComputeMethod]
    public virtual Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken)
        => ListDevices(userId, Symbol.Empty, null, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsThread(out var threadChatId)) {
            var subscriberIds = await ListSubscribedUserIds(threadChatId.ParentChatId, cancellationToken).ConfigureAwait(false);
            subscriberIds = await FilterByFollowThreadStatus(subscriberIds, chatId, cancellationToken).ConfigureAwait(false);
            subscriberIds = await FilterByNotificationMode(subscriberIds, chatId, cancellationToken).ConfigureAwait(false);
            return subscriberIds;
        }
        else {
            var subscriberIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
            subscriberIds = await FilterByNotificationMode(subscriberIds, chatId, cancellationToken).ConfigureAwait(false);
            return subscriberIds;
        }
    }

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<NotificationId>> ListRecentNotificationIds(
        UserId userId, Moment minSentAt, CancellationToken cancellationToken)
    {
        await PseudoListRecentNotificationIds(userId).ConfigureAwait(false);

        // Get notifications for last day
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return (
            from n in dbContext.Notifications
            where n.UserId == userId.Value && n.SentAt >= minSentAt.ToDateTimeClamped()
            orderby n.SentAt descending, n.Version descending, n.Id
            select NotificationId.Parse(n.Id)
            ).ToList();
    }

    // [ComputeMethod]
    public virtual async Task<UserNotificationInfo> GetUserNotificationInfo(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserNotifications = await dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbUserNotifications?.ToModel() ?? new UserNotificationInfo(userId);
    }

    // [CommandHandler]
    public virtual async Task OnNotify(NotificationsBackend_Notify command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var notification = command.Notification;
        var userId = notification.UserId.Require();
        if (notification.SentAt == default)
            notification = notification with { SentAt = Clocks.SystemClock.Now };

        DebugLog?.LogInformation("-> OnNotify. UserId={UserId}, NotificationId={NotificationId}",
            userId, notification.Id);

        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        if (info.IsDormant) {
            DebugLog?.LogInformation("OnNotify: skipped (dormant). UserId={UserId}, NotificationId={NotificationId}",
                userId, notification.Id);
            return;
        }

        if (IsSoftUpdate(info, notification)) {
            EnqueueSoft(userId, notification, info);
            return;
        }

        var batch = DrainSoftBuffer(userId);
        batch.Add(notification);
        await ApplyHardUpdate(userId, batch, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnProcess(NotificationsBackend_Process command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var userId = command.UserId;
        var batch = DrainSoftBuffer(userId);
        if (batch.Count == 0)
            return;

        DebugLog?.LogInformation("-> OnProcess. UserId={UserId}, Count={Count}", userId, batch.Count);
        await ApplyHardUpdate(userId, batch, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<bool> OnUpsert(NotificationsBackend_Upsert command, CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var sid = notification.Id.Value;
        var userId = notification.UserId.Require();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invIsCreate = context.Operation.Items.KeylessGet(false);
            if (invIsCreate) // Created
                _ = PseudoListRecentNotificationIds(userId);

            // Created or Updated
            _ = Get(notification.Id, default);
            return default;
        }

        try {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            var dbNotification = await dbContext.Notifications.ForUpdate()
                .FirstOrDefaultAsync(e => e.Id == sid, cancellationToken)
                .ConfigureAwait(false);

            if (dbNotification == null) {
                // Create
                notification = notification with {
                    Version = VersionGenerator.NextVersion(),
                    CreatedAt = notification.CreatedAt == default
                        ? notification.SentAt
                        : notification.CreatedAt,
                };
                dbNotification = new DbNotification();
                dbNotification.UpdateFrom(notification);
                dbContext.Notifications.Add(dbNotification);
                context.Operation.Items.KeylessSet(true);
            }
            else {
                // Update
                var throttleInterval = GetThrottleInterval(notification);
                if (notification.SentAt.ToDateTime() - dbNotification.SentAt < throttleInterval)
                    return false; // skip update and avoid sending notification if notification for the user has already been sent recently

                notification = notification with {
                    Version = VersionGenerator.NextVersion(notification.Version),
                };
                dbNotification.UpdateFrom(notification);
                context.Operation.Items.KeylessSet(false);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when(e.Entries.All(en => en.State == EntityState.Added)) {
            // Notification has already been created for another message, let's skip
            return false;
        }

        return true;
    }

    // [CommandHandler]
    public virtual async Task<bool> OnUpsertExplicitNotification(
        NotificationsBackend_UpsertExplicitNotification command,
        CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var sid = notification.Id.Value;

        if (Invalidation.IsActive) {
            // Created or Updated
            _ = GetExplicit(notification.Id, default);
            return default;
        }

        try {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            var dbNotification = await dbContext.ExplicitNotifications.ForUpdate()
                .FirstOrDefaultAsync(e => e.Id == sid, cancellationToken)
                .ConfigureAwait(false);

            var now = Clocks.SystemClock.Now;
            if (dbNotification == null) {
                // Create
                notification = notification with {
                    Version = VersionGenerator.NextVersion(),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                dbNotification = new DbExplicitNotification();
                dbNotification.UpdateFrom(notification);
                dbContext.ExplicitNotifications.Add(dbNotification);
            }
            else {
                // Update
                notification = notification with {
                    Version = VersionGenerator.NextVersion(notification.Version),
                    UpdatedAt = now
                };
                dbNotification.UpdateFrom(notification);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when(e.Entries.All(en => en.State == EntityState.Added)) {
            // Notification has already been created for another message, let's skip
            return false;
        }

        return true;
    }

    // [CommandHandler]
    public virtual async Task OnRegisterDevice(NotificationsBackend_RegisterDevice command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var device = context.Operation.Items.KeylessGet<DbDevice>();
            var isNew = context.Operation.Items.KeylessGet(false);
            if (isNew && device != null)
                _ = ListDevices(UserId.Parse(device.UserId), default);
            return;
        }

        var (userId, deviceId, deviceType, sessionHash) = command;
        DebugLog?.LogInformation("-> OnRegisterDevice. UserId={UserId}, DeviceId={DeviceId}, DeviceType={DeviceType}, SessionHash={SessionHash}",
            userId, deviceId, deviceType, sessionHash);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices.ForUpdate()
            .FirstOrDefaultAsync(d => d.Id == deviceId.Value, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingDbDevice;
        if (dbDevice == null) {
            dbDevice = new DbDevice {
                Id = deviceId,
                Type = deviceType,
                UserId = userId.Value,
                SessionHash = sessionHash,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbDevice);
        }
        else {
            DebugLog?.LogInformation("-- OnRegisterDevice. Existing DbDevice found:" +
                " UserId={UserId}, DeviceId={DeviceId}, DeviceType={DeviceType}, SessionHash={SessionHash}, AccessedAt={AccessedAt}",
                dbDevice.UserId, dbDevice.Id, dbDevice.Type, dbDevice.SessionHash, dbDevice.AccessedAt);
            dbDevice.AccessedAt = Clocks.SystemClock.Now;
            if (dbDevice.Type == DeviceType.WebBrowser && deviceType != DeviceType.WebBrowser)
                dbDevice.Type = deviceType; // Now MAUI app reports device type properly, lets update it.
            if (dbDevice.SessionHash.IsNullOrEmpty() && !sessionHash.IsEmpty)
                dbDevice.SessionHash = sessionHash;
            if (UserId.TryParse(dbDevice.UserId, out var existingUserId) && existingUserId != userId) {
                if (existingUserId.IsGuest) {
                    dbDevice.UserId = userId.Value;
                    DebugLog?.LogInformation("Guest UserId for Device '{DeviceId}' has been updated: '{OldUserId}'->'{NewUserId}'",
                        existingUserId, existingUserId, userId);
                }
                else
                    Log.LogWarning("User {UserId} is trying to register device for {ExistingUserId}. Skipped", userId, existingUserId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(dbDevice);
        context.Operation.Items.KeylessSet(existingDbDevice == null);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveDevices(NotificationsBackend_RemoveDevices command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invUserIds = context.Operation.Items.KeylessGet<HashSet<UserId>>();
            if (invUserIds is { Count: > 0 })
                foreach (var invUserId in invUserIds)
                    _ = ListDevices(invUserId, default);
            return;
        }

        var affectedUserIds = new HashSet<UserId>();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        foreach (var deviceId in command.DeviceIds) {
            var dbDevice = await dbContext.Devices
                .Get(deviceId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbDevice == null)
                continue;

            dbContext.Devices.Remove(dbDevice);
            affectedUserIds.Add(UserId.Parse(dbDevice.UserId));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", affectedUserIds.Count);
        context.Operation.Items.KeylessSet(affectedUserIds);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(NotificationsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var userId = command.UserId;
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var removedDeviceCount = await dbContext.Devices
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.Notifications
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", removedDeviceCount);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMembers(
        NotificationsBackend_NotifyMembers command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (userId, chatId, lastEntryLocalId) = command;
        var userIds = await ListSubscribedUserIds(chatId, cancellationToken).ConfigureAwait(false);
        await NotifyMembersInternal(userId, chatId, lastEntryLocalId, userIds, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMentionedMembers(NotificationsBackend_NotifyMentionedMembers command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (userId, ChatEntryId, mentionedUserIds) = command;
        var chatId = ChatEntryId.ChatId;

        var subscribedUserIds = await ListSubscribedUserIds(chatId, cancellationToken).ConfigureAwait(false);
        var userIds = subscribedUserIds.Intersect(mentionedUserIds).ToArray();
        if (userIds.Length == 0)
            return;

        await NotifyMembersInternal(userId, chatId, ChatEntryId.LocalId, userIds, cancellationToken).ConfigureAwait(false);
    }

    // Event handlers

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        if (!ShouldNotify(entry, oldEntry, changeKind))
            return;

        await SendChatMessageNotification(entry, author, cancellationToken).ConfigureAwait(false);
        return;

        // - plain typed text: Create already carries the full content → push immediately;
        // - audio / JustText voice: Create is empty + streaming, final content arrives only
        //   in the subsequent Update → push on the streaming → finalized transition.
        static bool ShouldNotify(ChatEntry entry, ChatEntry? oldEntry, ChangeKind changeKind) {
            if (changeKind == ChangeKind.Create)
                return !entry.IsContentStreaming;
            if (changeKind != ChangeKind.Update || oldEntry is null)
                return false;
            return oldEntry.IsContentStreaming && !entry.IsContentStreaming;
        }
    }

    // [EventHandler]
    public virtual async Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (reaction, entry, author, reactionAuthor, changeKind) = eventCommand;
        if (changeKind == ChangeKind.Remove)
            return;
        if (author.UserId.IsGuest) // No notifs for guests
            return;
        if (author.Id == reactionAuthor.Id) // No notifs on your own reactions to your own messages
            return;

        var (text, _) = await NotificationHelper.GetText(entry, MarkupConsumer.ReactionNotification, ChatMarkupHubFactory, cancellationToken).ConfigureAwait(false);
        if (!entry.Content.IsNullOrEmpty())
            text = $"\"{text}\"";
        text = $"{reaction.Emoji} to {text}";
        var userIds = new[] { author.UserId };
        var similarityKey = entry.ChatId.Value;
        await EnqueueMessageRelatedNotifications(
            entry.ChatId, entry.Id, reactionAuthor, text, NotificationKind.Reaction,
            similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnChatChangedEventEvent(
        ChatChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (chat, _, changeKind) = eventCommand;
        if (!(changeKind is ChangeKind.Create && chat.Id.IsThread(out var threadChatId)))
            return;

        // New thread has been created.
        var parentChatId = threadChatId.ParentChatId;
        var userIds = await ListSubscribedUserIds(parentChatId, cancellationToken).ConfigureAwait(false);
        var similarityKey = parentChatId.Value;
        var creator = await ChatThreadsBackend.GetThreadCreator(chat.Id, cancellationToken).ConfigureAwait(false);
        if (creator is null)
            return;

        var text = $"Thread '{chat.Title}' has been created";
        await EnqueueMessageRelatedNotifications(
                parentChatId, null, creator, text, NotificationKind.Thread, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnSignedOut(UserSignedOutEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = eventCommand.Session;
        var devices = await ListDevices(eventCommand.UserId, session.Hash, null, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0)
            return;

        var command = new NotificationsBackend_RemoveDevices(devices.Select(c => c.DeviceId).ToArray());
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    // [ComputeMethod]
    public virtual Task<Unit> PseudoListRecentNotificationIds(UserId userId)
        => ActualLab.Async.TaskExt.UnitTask;

    // Private methods

    private async Task SendChatMessageNotification(
        ChatEntry entry,
        AuthorFull author,
        CancellationToken cancellationToken)
    {
        var freshEntry = await ChatsBackend
            .GetEntry(entry.Id, Constants.Notification.EntryWaitTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (freshEntry is null)
            return;

        var entryId = freshEntry.Id;
        var chatId = entryId.ChatId;
        var (text, mentionIds) = await NotificationHelper
            .GetText(freshEntry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);
        var key = chatId.Id.Value;
        if (!_recentChatsWithNotifications.TryGetValue(key, out _)) {
            using ICacheEntry cacheEntry = _recentChatsWithNotifications.CreateEntry(key);
            cacheEntry.Size = 1;
            cacheEntry.Value = "";
            cacheEntry.AbsoluteExpirationRelativeToNow = Constants.Notification.ThrottleIntervals.Message;
        }
        else if (mentionIds.Count == 0) {
            DebugLog?.LogInformation("Throttle low priority notifications. EntryId={EntryId}", entryId);
            return;
        }
        var userIds = await ListSubscribedUserIds(chatId, cancellationToken).ConfigureAwait(false);
        await EnqueueMessageRelatedNotifications(
            chatId, entryId, author, text, NotificationKind.Message,
            chatId.Value, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task Send(UserId userId, Notification notification, int badgeCount, CancellationToken cancellationToken1)
    {
        var minActiveAt = Clocks.SystemClock.Now - Constants.Notification.ActiveDevicePeriod;
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken1).ConfigureAwait(false);
        if (devices.Count == 0) {
            Log.LogInformation("No recipient devices found for notification #{NotificationId}", notification.Id);
            return;
        }

        var account = await AccountsBackend.Get(userId, cancellationToken1).ConfigureAwait(false);
        var isAdmin = account is { IsAdmin: true };
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var entryId = GetEntryId(notification);
        DebugLog?.LogInformation("-> Send. EntryId={EntryId}, UserId={UserId}, NotificationId={Kind}, DeviceIds#={DeviceIdCount}",
            entryId, userId, notification.Id, deviceIds.Count);
        await FirebaseMessagingClient.SendMessage(notification, deviceIds, isAdmin, badgeCount, cancellationToken1).ConfigureAwait(false);
        DebugLog?.LogInformation("<- Send. EntryId={EntryId}, UserId={UserId}, NotificationId={Kind}, DeviceIds#={DeviceIdCount}",
            entryId, userId, notification.Id, deviceIds.Count);
    }

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatId chatId,
        ChatEntryId? entryId,
        AuthorFull changeAuthor,
        string content,
        NotificationKind kind,
        string similarityKey,
        IReadOnlyList<UserId> userIds,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("-> EnqueueMessageRelatedNotifications. ChatId={ChatId}, EntryId={EntryId}, Kind={Kind}, UserIds#={UserIdCount}",
            chatId, entryId, kind, userIds.Count);

        if (entryId is not null && entryId.ChatId != chatId)
            throw new ArgumentOutOfRangeException(nameof(entryId), "entry.ChatId should match given chatId");

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var title = NotificationHelper.GetTitle(chat, changeAuthor);
        var iconUrl = NotificationHelper.GetIconUrl(chat, changeAuthor, UrlMapper);
        var now = Clocks.CoarseSystemClock.Now;
        var otherUserIds = userIds.Where(userId => userId != changeAuthor.UserId);

        foreach (var otherUserId in otherUserIds) {
            var checkPresence = kind != NotificationKind.Attention;
            if (checkPresence) {
                var presence = await UserPresences.Get(otherUserId, cancellationToken).ConfigureAwait(false);
                // Delay notifications for online users — if still unread after delay, send anyway
                if (presence is Presence.Online or Presence.Recording) {
                    if (kind == NotificationKind.Message && entryId is not null) {
                        DebugLog?.LogInformation(
                            "EnqueueMessageRelatedNotifications. Scheduling delayed check for online user. ChatId={ChatId}, EntryId={EntryId}, UserId={UserId}",
                            chatId, entryId, otherUserId);
                        var flowArgs = NotificationFlow.GetArguments(otherUserId, chatId);
                        await FlowHub.NewResumeEvent<NotificationFlow>(flowArgs)
                            .WithDelay(Constants.Notification.OnlineCheckDelay)
                            .Schedule(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    continue;
                }
            }
            var entryLid = entryId?.LocalId ?? 0;
            var fullEntryId = entryId ?? ChatEntryId.New(chatId, entryLid);
            Notification notification = kind switch {
                NotificationKind.Message => MessageNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Reply => ReplyNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Thread => ThreadNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Invitation => InvitationNotification.New(otherUserId, chatId, changeAuthor.Id),
                NotificationKind.Mention => MentionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                NotificationKind.Reaction => ReactionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                NotificationKind.Attention => AttentionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                _ => throw StandardError.NotSupported<NotificationsBackend>($"Unsupported notification kind: {kind}."),
            };
            notification = notification with {
                Title = title,
                Text = content,
                IconUrl = iconUrl,
                SentAt = now,
            };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken).ConfigureAwait(false);
        }
    }


    private static TimeSpan? GetThrottleInterval(Notification notification)
    {
        if (notification.Kind == NotificationKind.Message)
            return Constants.Notification.ThrottleIntervals.Message;
        if (notification.Kind == NotificationKind.Reaction)
            return Constants.Notification.ThrottleIntervals.Message;

        return null;
    }

    private async Task<IReadOnlyList<Device>> ListDevices(
        UserId userId, Symbol sessionHash, Moment? minActiveAt, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        // AccessedAt is null until the same token re-registers, so freshness falls back to CreatedAt.
        var minActiveAtValue = minActiveAt?.ToDateTime() ?? default;
        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId.Value)
            .WhereIf(d => d.SessionHash == sessionHash.Value, !sessionHash.IsEmpty)
            .WhereIf(d => (d.AccessedAt ?? d.CreatedAt) >= minActiveAtValue, minActiveAt.HasValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = dbDevices.Select(d => d.ToModel()).ToList();
        return devices;
    }

    private async Task NotifyMembersInternal(
        UserId userId, ChatId chatId, long textEntryLid, IReadOnlyList<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var now = Clocks.CoarseSystemClock.Now;
        var similarityKey = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var content = $"{author.Avatar.Name} asks for attention";
        var lastEntryId = ChatEntryId.New(chatId, textEntryLid);
        await EnqueueMessageRelatedNotifications(
            chatId, lastEntryId, author, content, NotificationKind.Attention,
            similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UserId[]> FilterByNotificationMode(IReadOnlyList<UserId> userIds, ChatId chatId, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];

        var notificationModes = await userIds
            .Select(async userId => {
                var kvas = ServerKvasBackend.ForUser(userId);
                var notificationMode = await kvas.ChatUserSettings(chatId).Get(x => x.NotificationMode, cancellationToken).ConfigureAwait(false);
                return (UserId: userId, NotificationMode: notificationMode);
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var subscriberIds = notificationModes
            .Where(kv => kv.NotificationMode != ChatNotificationMode.Muted)
            .Select(kv => kv.UserId)
            .ToArray();
        return subscriberIds;
    }

    private async Task<UserId[]> FilterByFollowThreadStatus(IReadOnlyList<UserId> userIds, ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return Array.Empty<UserId>();

        var subscriberIdWithStatus = await userIds
            .Select<UserId, Task<(UserId, bool)>>(async subscriberId  => {
                var contactId = ContactId.NewAny(subscriberId, chatId);
                var threadContact = await ContactsBackend
                    .GetThreadContact(subscriberId, contactId, cancellationToken)
                    .ConfigureAwait(false);
                var isFollowingThread = threadContact is not null;
                return (subscriberId, isFollowingThread);
            })
            .Collect(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);
        return subscriberIdWithStatus
            .Where(c => c.Item2)
            .Select(c => c.Item1)
            .ToArray();
    }

    private static ChatEntryId? GetEntryId(Notification notification)
        => notification switch {
            ChatEntryRelatedNotification n => n.EntryId,
            ChatEntryNotification n => n.EntryId,
            _ => null,
        };

    private static bool IsSoftUpdate(UserNotificationInfo info, Notification notification)
    {
        var hasSimilar = info.Displayed.Any(n => n.Id == notification.Id);
        if (!hasSimilar)
            return false; // First notification for this key -> hard update

        if (notification is ChatEntryNotification)
            return false; // Individually-seen (mention / reaction / attention) -> hard update

        var sinceLastPush = notification.SentAt - info.LastPushAt;
        return sinceLastPush <= Constants.Notification.SilencePeriod;
    }

    private void EnqueueSoft(UserId userId, Notification notification, UserNotificationInfo info)
    {
        var buffer = _softBuffers.GetOrAdd(userId, static _ => new SoftBuffer());
        bool mustSchedule;
        lock (buffer.Lock) {
            buffer.Pending.Add(notification);
            mustSchedule = !buffer.IsProcessScheduled;
            buffer.IsProcessScheduled = true;
        }
        if (!mustSchedule)
            return;

        var delay = Constants.Notification.SilencePeriod - (Clocks.SystemClock.Now - info.LastPushAt);
        ScheduleProcess(userId, delay);
    }

    private void ScheduleProcess(UserId userId, TimeSpan delay)
        => _ = Task.Run(async () => {
            try {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay).ConfigureAwait(false);
                await Commander.Call(new NotificationsBackend_Process(userId), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "Deferred notification processing failed for UserId={UserId}", userId);
            }
        });

    private List<Notification> DrainSoftBuffer(UserId userId)
    {
        if (!_softBuffers.TryGetValue(userId, out var buffer))
            return [];

        lock (buffer.Lock) {
            buffer.IsProcessScheduled = false;
            if (buffer.Pending.Count == 0)
                return [];

            var batch = new List<Notification>(buffer.Pending);
            buffer.Pending.Clear();
            return batch;
        }
    }

    private async Task ApplyHardUpdate(
        UserId userId, IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var dbUserNotifications = await dbContext.UserNotifications.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);

        UserNotificationInfo info;
        if (dbUserNotifications != null) {
            info = Apply(dbUserNotifications.ToModel());
            dbUserNotifications.UpdateFrom(info);
        }
        else {
            info = Apply(new UserNotificationInfo(userId));
            dbUserNotifications = new DbUserNotifications();
            dbUserNotifications.UpdateFrom(info);
            dbContext.UserNotifications.Add(dbUserNotifications);
        }

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when (e.Entries.All(en => en.State == EntityState.Added)) {
            // Lost the create race (INSERT ... ON CONFLICT DO NOTHING affected 0 rows):
            // the row exists now -> re-read it under lock and apply as an update instead.
            dbContext.Entry(dbUserNotifications).State = EntityState.Detached;
            dbUserNotifications = await dbContext.UserNotifications.ForUpdate()
                .FirstAsync(x => x.Id == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            info = Apply(dbUserNotifications.ToModel());
            dbUserNotifications.UpdateFrom(info);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin())
                _ = GetUserNotificationInfo(userId, default);
            return Task.CompletedTask;
        });

        await Send(userId, notifications[^1], info.Displayed.Count, cancellationToken).ConfigureAwait(false);
        return;

        UserNotificationInfo Apply(UserNotificationInfo current) {
            foreach (var notification in notifications)
                current = current.WithNotification(notification);
            return current with {
                Version = VersionGenerator.NextVersion(current.Version),
                LastPushAt = Clocks.SystemClock.Now,
                IsDormant = current.IsDormant || current.Displayed.Count >= Constants.Notification.DormancyThreshold,
            };
        }
    }

    private sealed class SoftBuffer
    {
        public readonly Lock Lock = new();
        public readonly List<Notification> Pending = [];
        public bool IsProcessScheduled;
    }
}
