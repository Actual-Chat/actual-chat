using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using ActualChat.Contacts;
using ActualChat.Db;
using ActualChat.Notifications.Db;
using ActualChat.Queues;
using ActualChat.Sharding;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Notifications;

/// <summary>
/// Backend service implementation for managing push notifications and device tokens.
/// </summary>
public class NotificationsBackend(IServiceProvider services)
    : ShardedDbServiceBase<NotificationDbContext>(services), INotificationsBackend
{
    // Per-user soft-update buffers, owned by this shard. Entries are lost on restart by design
    // (see docs/plans/notif-api.md); a committed hard update always re-reads from the DB.
    private readonly ConcurrentDictionary<UserId, SoftBuffer> _softBuffers = new();

    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private Streaming.ILiveConversationsBackend LiveConversationsBackend { get; } = services.GetRequiredService<Streaming.ILiveConversationsBackend>();
    private IChatThreadsBackend ChatThreadsBackend { get; } = services.GetRequiredService<IChatThreadsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IChatPositionsBackend ChatPositionsBackend { get; } = services.GetRequiredService<IChatPositionsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private IDbEntityResolver<string, DbExplicitNotification> DbExplicitNotificationResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExplicitNotification>>();

    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private IFirebaseMessagingClient FirebaseMessagingClient { get; }
        = services.GetRequiredService<IFirebaseMessagingClient>();
    private IQueues Queues { get; } = services.Queues();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private CancellationToken StopToken { get; } = services.GetService<IHostApplicationLifetime>().StopToken();
    private ILogger? DebugLog => Log;

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
    public virtual async Task<UserNotificationInfo> GetUserNotificationInfo(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserNotifications = await dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        var info = dbUserNotifications?.ToModel() ?? new UserNotificationInfo(userId);

        // Population re-check: hide notifications the user has since read, or whose chat the user
        // has muted (mute may happen after the notification was shown). This is the single source
        // of truth for the "active" set — so the in-app list, the badge count, and the client
        // reconciler all agree, and it matches the unmuted count the push path sends.
        // Reads IChatPositionsBackend.Get + mute mode (once per distinct chat), so Fusion
        // re-invalidates this method whenever a read position or mute setting changes.
        var readPositions = await GetReadPositions(userId, info.Displayed, cancellationToken).ConfigureAwait(false);
        var mutedChatIds = await GetMutedChatIds(userId, info.Displayed, cancellationToken).ConfigureAwait(false);
        var displayed = info.Displayed.Without(n => IsRead(n, readPositions) || IsMutedChat(n, mutedChatIds));
        if (displayed.Count == info.Displayed.Count)
            return info;

        return info with {
            Displayed = displayed,
            IsDormant = displayed.Count >= Constants.Notification.DormancyThreshold,
        };
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
        await ApplyHardUpdate(userId, batch, [], cancellationToken).ConfigureAwait(false);
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
        await ApplyHardUpdate(userId, batch, [], cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnHandle(NotificationsBackend_Handle command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var notificationId = command.NotificationId;
        DebugLog?.LogInformation("-> OnHandle. NotificationId={NotificationId}", notificationId);
        await ApplyHardUpdate(notificationId.UserId, [], [notificationId], cancellationToken).ConfigureAwait(false);
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
        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var removedDeviceCount = await dbContext.Devices
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.UserNotifications
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin()) {
                _ = GetUserNotificationInfo(userId, default);
                _ = ListDevices(userId, default);
            }
            return Task.CompletedTask;
        });
        Log.LogInformation("Removed account notification data: {DeviceCount} device(s), UserId={UserId}",
            removedDeviceCount, userId);
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

    // [CommandHandler]
    public virtual async Task OnNotifyLiveConversation(
        NotificationsBackend_NotifyLiveConversation command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (chatId, content, isFinal, startEntryLid) = command;
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var userIds = await ListSubscribedUserIds(chatId, cancellationToken).ConfigureAwait(false);
        var phase = isFinal ? "final" : "start";
        var similarityKey = $"{chatId.Value}:live:{startEntryLid}:{phase}";
        var now = Clocks.CoarseSystemClock.Now;
        foreach (var userId in userIds) {
            // Joined users (and streamers, who signal participation) already see the call live.
            if (await LiveConversationsBackend.IsParticipant(chatId, userId, cancellationToken).ConfigureAwait(false))
                continue;

            var notificationId = NotificationId.New(userId, NotificationKind.Message, similarityKey);
            var notification = new Notification(notificationId) {
                Title = chat.Title,
                Content = content,
                SentAt = now,
                ChatNotification = new ChatNotificationOption(chatId),
            };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken).ConfigureAwait(false);
        }
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

        // Suppress per-message notifications for entries inside an active live conversation —
        // non-joined users get only the START/FINAL notifications, joined users get none.
        var live = await LiveConversationsBackend.Get(entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (live is { } lc && entry.LocalId >= lc.StartEntryLid)
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
    // Reconciles read notifications promptly when a Read position advances, so a notification
    // read on one device is dropped (and a silent dismissal pushed) on the others without waiting
    // for the user's next notification event.
    public virtual async Task OnReadPositionChangedEvent(ReadPositionChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (userId, chatId, _) = eventCommand;

        // Cheap gate: avoid the operation + row lock + push path unless this user has a displayed
        // notification anchored to this chat. The event's EntryLid is advisory (collapsed events
        // keep the window's first advance) — ApplyHardUpdate re-checks the live Read position.
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbUserNotifications = await dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserNotifications is null)
            return;

        var hasNotificationInChat = dbUserNotifications.ToModel().Displayed.Any(n => {
            var (anchorChatId, anchorEntryLid) = GetReadAnchor(n);
            return anchorChatId == chatId && anchorEntryLid > 0;
        });
        if (!hasNotificationInChat)
            return;

        DebugLog?.LogInformation("-> OnReadPositionChangedEvent. UserId={UserId}, ChatId={ChatId}", userId, chatId);
        await ApplyHardUpdate(userId, [], [], cancellationToken).ConfigureAwait(false);
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
        var (text, _) = await NotificationHelper
            .GetText(freshEntry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);
        var userIds = await ListSubscribedUserIds(chatId, cancellationToken).ConfigureAwait(false);
        await EnqueueMessageRelatedNotifications(
            chatId, entryId, author, text, NotificationKind.Message,
            chatId.Value, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    // The actual FCM send, run as a queued command so NATS retries it on transient failure and
    // it isn't lost if the pushing node dies mid-send.
    public virtual async Task OnPush(NotificationsBackend_Push command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // No state change, nothing to invalidate

        var notification = command.Notification;
        var userId = notification.UserId.Require();

        // Re-read current state at delivery: skip if the notification is no longer active (read,
        // handled, or muted since it was enqueued), and take the badge from the current active
        // set — so a redelivered/out-of-order queue message can't resurrect it or stamp a stale badge.
        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        if (!info.Displayed.Any(n => n.Id == notification.Id))
            return;

        var minActiveAt = Clocks.SystemClock.Now - Constants.Notification.ActiveDevicePeriod;
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0) {
            Log.LogInformation("No recipient devices found for notification #{NotificationId}", notification.Id);
            return;
        }

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        var isAdmin = account is { IsAdmin: true };
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var entryId = GetEntryId(notification);
        DebugLog?.LogInformation("-> OnPush. EntryId={EntryId}, UserId={UserId}, NotificationId={NotificationId}, DeviceIds#={DeviceIdCount}",
            entryId, userId, notification.Id, deviceIds.Count);
        await FirebaseMessagingClient.SendMessage(notification, deviceIds, isAdmin, info.Displayed.Count, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPushDismissal(NotificationsBackend_PushDismissal command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // No state change, nothing to invalidate

        var (userId, dismissed) = command;
        var minActiveAt = Clocks.SystemClock.Now - Constants.Notification.ActiveDevicePeriod;
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0)
            return;

        // Badge recomputed from current state at delivery (see OnPush).
        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        DebugLog?.LogInformation("-> OnPushDismissal. UserId={UserId}, Notifications#={Count}, DeviceIds#={DeviceIdCount}",
            userId, dismissed.Count, deviceIds.Count);
        await FirebaseMessagingClient.SendDismissal(dismissed, deviceIds, info.Displayed.Count, cancellationToken).ConfigureAwait(false);
    }

    // Fetches each distinct chat's Read position once (in parallel) instead of one sequential
    // round-trip per notification. Read-state is then evaluated per notification *instance*
    // against its own EntryLid — two notifications can share a NotificationId (chat-keyed dedup)
    // yet anchor to different entries.
    private async Task<IReadOnlyDictionary<ChatId, long>> GetReadPositions(
        UserId userId, IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        var chatIds = notifications
            .Select(n => GetReadAnchor(n).ChatId)
            .Where(c => c is not null)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
            return ReadOnlyDictionary<ChatId, long>.Empty;

        var positions = await chatIds
            .Select(async chatId => (
                ChatId: chatId!,
                ReadEntryLid: (await ChatPositionsBackend
                    .Get(userId, chatId!, ChatPositionKind.Read, cancellationToken)
                    .ConfigureAwait(false)).EntryLid))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return positions.ToDictionary(x => x.ChatId, x => x.ReadEntryLid);
    }

    // A chat notification is read once the user's Read position has advanced past its entry.
    private static bool IsRead(Notification notification, IReadOnlyDictionary<ChatId, long> readPositions)
    {
        var (chatId, entryLid) = GetReadAnchor(notification);
        if (chatId is null || entryLid <= 0)
            return false;
        if (!readPositions.TryGetValue(chatId, out var readEntryLid))
            return false;
        // A read position of 0 means "never read"; long.MaxValue is the client's "unbounded"
        // sentinel that must never gate a real entry (it would suppress every notification in the
        // chat forever). Treat both as "not read".
        if (readEntryLid is <= 0 or long.MaxValue)
            return false;
        return readEntryLid >= entryLid;
    }

    // Returns the muted chats among the notifications' chats, reading mute mode once per distinct
    // chat (in parallel).
    private async Task<HashSet<ChatId>> GetMutedChatIds(
        UserId userId, IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        var chatIds = notifications
            .Select(n => GetReadAnchor(n).ChatId)
            .Where(c => c is not null)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
            return [];

        var muteByChat = await chatIds
            .Select(async chatId => (
                ChatId: chatId!,
                IsMuted: await IsMuted(userId, chatId!, cancellationToken).ConfigureAwait(false)))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return muteByChat.Where(x => x.IsMuted).Select(x => x.ChatId).ToHashSet();
    }

    private static bool IsMutedChat(Notification notification, HashSet<ChatId> mutedChatIds)
    {
        var chatId = GetReadAnchor(notification).ChatId;
        return chatId is not null && mutedChatIds.Contains(chatId);
    }

    private async Task<bool> IsMuted(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        var kvas = ServerKvasBackend.ForUser(userId);
        var notificationMode = await kvas.ChatUserSettings(chatId)
            .Get(x => x.NotificationMode, cancellationToken)
            .ConfigureAwait(false);
        return notificationMode == ChatNotificationMode.Muted;
    }

    private static (ChatId? ChatId, long EntryLid) GetReadAnchor(Notification notification)
        => notification switch {
            ChatEntryRelatedNotification n => (n.ChatId, n.EntryLid),
            ChatEntryNotification n => (n.ChatId, n.EntryLid),
            _ => (null, 0L),
        };

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
        bool mustSchedule;
        while (true) {
            var buffer = _softBuffers.GetOrAdd(userId, static _ => new SoftBuffer());
            lock (buffer.Lock) {
                if (buffer.IsRemoved)
                    continue; // a concurrent drain evicted this instance -> get/create a fresh one
                buffer.Pending.Add(notification);
                mustSchedule = !buffer.IsProcessScheduled;
                buffer.IsProcessScheduled = true;
            }
            break;
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
                    await Task.Delay(delay, StopToken).ConfigureAwait(false);
                await Commander.Call(new NotificationsBackend_Process(userId), StopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Host is shutting down; the soft buffer is in-memory and intentionally transient.
            }
            catch (Exception e) {
                Log.LogError(e, "Deferred notification processing failed for UserId={UserId}", userId);
            }
        }, StopToken);

    private List<Notification> DrainSoftBuffer(UserId userId)
    {
        if (!_softBuffers.TryGetValue(userId, out var buffer))
            return [];

        lock (buffer.Lock) {
            buffer.IsProcessScheduled = false;
            var batch = new List<Notification>(buffer.Pending);
            buffer.Pending.Clear();
            // Evict the now-drained buffer so the map stays bounded by users with in-flight
            // soft updates. IsRemoved makes a racing EnqueueSoft retry with a fresh instance.
            buffer.IsRemoved = true;
            _softBuffers.TryRemove(new KeyValuePair<UserId, SoftBuffer>(userId, buffer));
            return batch;
        }
    }

    // The single DB-write + push path. Adds notifications, drops notifications whose entry
    // has been read or that were explicitly handled, then pushes the resulting delta.
    private async Task ApplyHardUpdate(
        UserId userId,
        IReadOnlyList<Notification> notifications,
        IReadOnlyCollection<NotificationId> handledIds,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var dbUserNotifications = await dbContext.UserNotifications.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);

        var (info, dismissed) = await Reconcile(
            dbUserNotifications?.ToModel() ?? new UserNotificationInfo(userId)).ConfigureAwait(false);
        if (notifications.Count == 0 && dismissed.Count == 0)
            return; // Nothing changed (e.g. OnHandle for an already-dismissed notification)

        if (dbUserNotifications != null)
            dbUserNotifications.UpdateFrom(info);
        else {
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
            (info, dismissed) = await Reconcile(dbUserNotifications.ToModel()).ConfigureAwait(false);
            dbUserNotifications.UpdateFrom(info);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin())
                _ = GetUserNotificationInfo(userId, default);
            return Task.CompletedTask;
        });

        // Push one banner per distinct chat (= client tag): a coalesced batch can add
        // notifications for several chats, and a single push would silently drop the others'
        // banners while the badge still counts them.
        // Pushes go out as operation events (the transactional outbox): they're persisted to
        // _events in this same commit and DbEventForwarder hands them to the queue, so a push
        // is never lost if this node dies after the commit. The badge is NOT snapshotted here —
        // OnPush/OnPushDismissal recompute it from current state at delivery, so out-of-order or
        // redelivered queue messages can't stamp a stale count.
        var toPush = notifications
            .Where(n => info.Displayed.Any(d => d.Id == n.Id))
            .GroupBy(GetPushGroupKey)
            .Select(g => g.MaxBy(n => n.SentAt)!)
            .ToList();
        foreach (var notification in toPush)
            context.Operation.AddEvent(new NotificationsBackend_Push(notification));
        if (dismissed.Count > 0) {
            // Only close banners whose tag is now fully gone — a chat may still have another
            // active notification under the same tag (e.g. a message remains after a mention is
            // read). The badge is still refreshed by the dismissal push regardless.
            var survivingTags = info.Displayed.Select(GetPushGroupKey).ToHashSet(StringComparer.Ordinal);
            var bannersToClose = dismissed.Where(d => !survivingTags.Contains(GetPushGroupKey(d))).ToApiArray();
            context.Operation.AddEvent(new NotificationsBackend_PushDismissal(userId, bannersToClose));
        }
        return;

        async Task<(UserNotificationInfo Info, IReadOnlyList<Notification> Dismissed)> Reconcile(UserNotificationInfo committed)
        {
            var readPositions = await GetReadPositions(userId, committed.Displayed.Concat(notifications), cancellationToken)
                .ConfigureAwait(false);
            var current = committed;
            var dismissed = new List<Notification>();
            foreach (var existing in committed.Displayed) {
                var isGone = handledIds.Contains(existing.Id) || IsRead(existing, readPositions);
                if (isGone) {
                    current = current with { Displayed = current.Displayed.Without(x => x.Id == existing.Id) };
                    dismissed.Add(existing);
                }
            }
            foreach (var notification in notifications)
                if (!IsRead(notification, readPositions))
                    current = current.WithNotification(notification);
            current = current with {
                Version = VersionGenerator.NextVersion(current.Version),
                LastPushAt = notifications.Count > 0 ? Clocks.SystemClock.Now : current.LastPushAt,
                IsDormant = current.Displayed.Count >= Constants.Notification.DormancyThreshold,
            };
            return (current, dismissed);
        }
    }

    // Banner grouping key = the client push tag. Uses the shared NotificationExt.GetChatTag so
    // the server's grouping and the client reconciler's matching can't drift; non-chat
    // notifications fall back to the dedup key.
    private static string GetPushGroupKey(Notification notification)
        => notification.GetChatTag() ?? notification.SimilarityKey;

    private sealed class SoftBuffer
    {
        public readonly Lock Lock = new();
        public readonly List<Notification> Pending = [];
        public bool IsProcessScheduled;
        public bool IsRemoved;
    }
}
