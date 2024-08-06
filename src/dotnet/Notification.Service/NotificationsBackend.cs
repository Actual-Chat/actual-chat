using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Db;
using ActualChat.Notification.Db;
using ActualChat.Queues;
using ActualChat.Users;
using ActualChat.Users.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Notification;

#pragma warning disable CA1001 // Has disposable _recentChatsWithNotifications
public class NotificationsBackend(IServiceProvider services)
    : DbServiceBase<NotificationDbContext>(services), INotificationsBackend
#pragma warning restore CA1001
{
    private readonly MemoryCache _recentChatsWithNotifications = new(new MemoryCacheOptions {
        CompactionPercentage = 0.1,
        SizeLimit = 10_000,
        ExpirationScanFrequency = TimeSpan.FromSeconds(5),
    });

    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private IDbEntityResolver<string, DbNotification> DbNotificationResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbNotification>>();

    private IUserPresences UserPresences { get; } = services.GetRequiredService<IUserPresences>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private FirebaseMessagingClient FirebaseMessagingClient { get; }
        = services.GetRequiredService<FirebaseMessagingClient>();
    private IQueues Queues { get; } = services.Queues();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private ILogger? DebugLog => Log;

    // [ComputeMethod]
    public virtual async Task<Notification?> Get(
        NotificationId notificationId,
        CancellationToken cancellationToken)
    {
        var dbNotification = await DbNotificationResolver.Get(notificationId, cancellationToken).ConfigureAwait(false);
        return dbNotification?.ToModel();
    }

    // [ComputeMethod]
    public virtual Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken)
        => ListDevices(userId, Symbol.Empty, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var userIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
        var notificationModes = await userIds
            .Select(async userId => {
                var kvas = ServerKvasBackend.GetUserClient(userId);
                var userChatSettings = await kvas.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
                return (UserId: userId, userChatSettings.NotificationMode);
            })
            .Collect()
            .ConfigureAwait(false);

        var subscriberIds = notificationModes
            .Where(kv => kv.NotificationMode != ChatNotificationMode.Muted)
            .Select(kv => kv.UserId)
            .ToList();
        return subscriberIds;
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
            where n.UserId == userId && n.SentAt >= minSentAt.ToDateTimeClamped()
            orderby n.SentAt descending, n.Version descending, n.Id
            select new NotificationId(n.Id)
            ).ToList();
    }

    // [CommandHandler]
    public virtual async Task OnNotify(NotificationsBackend_Notify command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var notification = command.Notification;
        var userId = notification.UserId.Require();

        DebugLog?.LogInformation("-> OnNotify. EntryId={EntryId}, UserId={UserId}, NotificationId={NotificationId}",
            notification.EntryId, userId, notification.Id);

        var similar = await Get(notification.Id, cancellationToken).ConfigureAwait(false);
        if (similar != null) {
            var throttleInterval = GetThrottleInterval(notification);
            if (throttleInterval is { } vThrottleInterval) {
                var delta = notification.SentAt - similar.SentAt;
                if (delta <= vThrottleInterval) {
                    DebugLog?.LogInformation("OnNotify. Skipping (Throttling). EntryId={EntryId}, UserId={UserIdsCount}, NotificationId={NotificationId}",
                        notification.EntryId, userId, notification.Id);
                    return;
                }
            }
            notification = notification.WithSimilar(similar);
        }

        var upsertCommand = new NotificationsBackend_Upsert(notification);
        var hasUpserted = await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        if (!hasUpserted) {
            DebugLog?.LogInformation("OnNotify. Skipping (Upsert failed). EntryId={EntryId}, UserId={UserIdsCount}, NotificationId={NotificationId}",
                notification.EntryId, userId, notification.Id);
            return;
        }

        await Send(userId, notification, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<bool> OnUpsert(NotificationsBackend_Upsert command, CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var sid = notification.Id.Value;
        var userId = notification.UserId.Require();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invIsCreate = context.Operation.Items.GetOrDefault(false);
            if (invIsCreate) // Created
                _ = PseudoListRecentNotificationIds(userId);

            // Created or Updated
            _ = Get(notification.Id, default);
            return default;
        }

        try {
            var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
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
                context.Operation.Items.Set(true);
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
                context.Operation.Items.Set(false);
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
            var device = context.Operation.Items.Get<DbDevice>();
            var isNew = context.Operation.Items.GetOrDefault(false);
            if (isNew && device != null)
                _ = ListDevices(new UserId(device.UserId), default);
            return;
        }

        var (userId, deviceId, deviceType, sessionHash) = command;
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices.ForUpdate()
            .FirstOrDefaultAsync(d => d.Id == deviceId.Value, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingDbDevice;
        if (dbDevice == null) {
            dbDevice = new DbDevice {
                Id = deviceId,
                Type = deviceType,
                UserId = userId,
                SessionHash = sessionHash,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbDevice);
        }
        else {
            dbDevice.AccessedAt = Clocks.SystemClock.Now;
            if (dbDevice.Type == DeviceType.WebBrowser && deviceType != DeviceType.WebBrowser)
                dbDevice.Type = deviceType; // Now maui app reports device type properly, lets update it.
            if (dbDevice.SessionHash.IsNullOrEmpty() && !sessionHash.IsEmpty)
                dbDevice.SessionHash = sessionHash;
            if (UserId.TryParse(dbDevice.UserId, out var existingUserId) && existingUserId != userId) {
                if (existingUserId.IsGuest)
                    dbDevice.UserId = userId;
                else
                    Log.LogWarning("User {UserId} is trying to register device for {ExistingUserId}. Skipped", userId, existingUserId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.Set(dbDevice);
        context.Operation.Items.Set(existingDbDevice == null);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveDevices(NotificationsBackend_RemoveDevices command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invUserIds = context.Operation.Items.Get<HashSet<UserId>>();
            if (invUserIds is { Count: > 0 })
                foreach (var invUserId in invUserIds)
                    _ = ListDevices(invUserId, default);
            return;
        }

        var affectedUserIds = new HashSet<UserId>();
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        foreach (var deviceId in command.DeviceIds) {
            var dbDevice = await dbContext.Devices
                .Get(deviceId, cancellationToken)
                .ConfigureAwait(false);
            if (dbDevice == null)
                continue;

            dbContext.Devices.Remove(dbDevice);
            affectedUserIds.Add(new UserId(dbDevice.UserId));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", affectedUserIds.Count);
        context.Operation.Items.Set(affectedUserIds);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(NotificationsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var userId = command.UserId;
        var dbContext = await DbHub.CreateDbContext(readWrite: true, cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var removedDevicesCount = await dbContext.Devices
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.Notifications
            .Where(a => a.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", removedDevicesCount);
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

        var author = await AuthorsBackend
            .GetByUserId(chatId, userId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var now = Clocks.CoarseSystemClock.Now;
        var similarityKey = now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var content = $"{author.Avatar.Name} asks for attention";
        var lastEntryId = (ChatEntryId)new TextEntryId(chatId, lastEntryLocalId, AssumeValid.Option);
        await EnqueueMessageRelatedNotifications(
                chatId, lastEntryId, author, content, NotificationKind.GetAttention, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // Event handlers

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        var isTranscribedTextEntry = entry.AudioEntryId.HasValue;
        if (isTranscribedTextEntry) {
            if (changeKind != ChangeKind.Update)
                return;
            // When transcribed message is being finalized, it's updated to IsStreaming = false.
            // At this moment we can notify chat users.
            var hasFinalized = oldEntry is { IsStreaming: true } && !entry.IsStreaming;
            if (!hasFinalized)
                return;
        }
        else {
            if (changeKind != ChangeKind.Create)
                return;
            // For regular text messages we notify chat users upon message creation.
        }

        // force loading entry media info
        entry = await ChatsBackend.GetEntry(entry.Id, Constants.Invalidation.Delay, cancellationToken).Require().ConfigureAwait(false);
        var (text, mentionIds) = await GetText(entry, MarkupConsumer.Notification, cancellationToken).ConfigureAwait(false);
        var chatId = entry.ChatId;
        var key = chatId.Id.Value;
        if (!_recentChatsWithNotifications.TryGetValue(key, out _)) {
            using ICacheEntry cacheEntry = _recentChatsWithNotifications.CreateEntry(key);
            cacheEntry.Size = 1;
            cacheEntry.Value = "";
            cacheEntry.AbsoluteExpirationRelativeToNow = Constants.Notification.ThrottleIntervals.Message;
        }
        else if (mentionIds.Count == 0) {
            // throttle low priority notifications
            DebugLog?.LogInformation("Throttle low priority notifications. EntryId={EntryId}", entry.Id);
            return;
        }

        var userIds = await ListSubscribedUserIds(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var similarityKey = entry.ChatId;
        await EnqueueMessageRelatedNotifications(
                entry.ChatId, entry.Id, author, text, NotificationKind.Message, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (reaction, entry, author, reactionAuthor, changeKind) = eventCommand;
        if (changeKind == ChangeKind.Remove)
            return;
        if (author.UserId.IsNone) // No notifs to anonymous users
            return;
        if (author.Id == reactionAuthor.Id) // No notifs on your own reactions to your own messages
            return;

        var (text, _) = await GetText(entry, MarkupConsumer.ReactionNotification, cancellationToken).ConfigureAwait(false);
        if (!entry.Content.IsNullOrEmpty())
            text = $"\"{text}\"";
        text = $"{reaction.EmojiId} to {text}";
        var userIds = new[] { author.UserId };
        var similarityKey = entry.ChatId;
        await EnqueueMessageRelatedNotifications(
                entry.ChatId, entry.Id, reactionAuthor, text, NotificationKind.Reaction, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnSignedOut(
        UserSignedOutEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var sessionId = eventCommand.SessionId;
        var session = new Session(sessionId);
        var devices = await ListDevices(eventCommand.UserId, session.Hash, cancellationToken).ConfigureAwait(false);
        if (devices.Count == 0)
            return;

        var command = new NotificationsBackend_RemoveDevices(devices.Select(c => c.DeviceId).ToApiArray());
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    // [ComputeMethod]
    public virtual Task<Unit> PseudoListRecentNotificationIds(UserId userId)
        => ActualLab.Async.TaskExt.UnitTask;

    // Private methods

    private async Task Send(UserId userId, Notification notification, CancellationToken cancellationToken1)
    {
        var devices = await ListDevices(userId, cancellationToken1).ConfigureAwait(false);
        if (devices.Count == 0) {
            Log.LogInformation("No recipient devices found for notification #{NotificationId}", notification.Id);
            return;
        }

        var account = await AccountsBackend.Get(userId, cancellationToken1).ConfigureAwait(false);
        var isAdmin = account is { IsAdmin: true };
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        DebugLog?.LogInformation("-> Send. EntryId={EntryId}, UserId={UserId}, NotificationId={Kind}, DeviceIds#={DeviceIdsCount}",
            notification.EntryId, userId, notification.Id, deviceIds.Count);
        await FirebaseMessagingClient.SendMessage(notification, deviceIds, isAdmin, cancellationToken1).ConfigureAwait(false);
        DebugLog?.LogInformation("<- Send. EntryId={EntryId}, UserId={UserId}, NotificationId={Kind}, DeviceIds#={DeviceIdsCount}",
            notification.EntryId, userId, notification.Id, deviceIds.Count);
    }

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatId chatId,
        ChatEntryId? entryId,
        AuthorFull changeAuthor,
        string content,
        NotificationKind kind,
        Symbol similarityKey,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("-> EnqueueMessageRelatedNotifications. ChatId={ChatId}, EntryId={EntryId}, Kind={Kind}, UserIds#={UserIdsCount}",
            chatId, entryId, kind, userIds.Count);

        if (entryId.HasValue && entryId.Value.ChatId != chatId)
            throw new ArgumentOutOfRangeException(nameof(entryId), "entry.ChatId should match given chatId");

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, changeAuthor);
        var iconUrl = GetIconUrl(chat, changeAuthor);
        var now = Clocks.CoarseSystemClock.Now;
        var otherUserIds = changeAuthor.UserId.IsNone ? userIds : userIds.Where(uid => uid != changeAuthor.UserId);

        foreach (var otherUserId in otherUserIds) {
            var checkPresence = kind != NotificationKind.GetAttention;
            if (checkPresence) {
                var presence = await UserPresences.Get(otherUserId, cancellationToken).ConfigureAwait(false);
                // Do not send notifications to users who are online
                if (presence is Presence.Online or Presence.Recording) {
                    DebugLog?.LogInformation(
                        "EnqueueMessageRelatedNotifications. Skipping online user. ChatId={ChatId}, EntryId={EntryId}, UserId={UserId}",
                        chatId, entryId, otherUserId);
                    continue;
                }
            }
            var notificationId = new NotificationId(otherUserId, kind, similarityKey);
            var notification = new Notification(notificationId) {
                Title = title,
                Content = content,
                IconUrl = iconUrl,
                SentAt = now,
            };
            if (kind == NotificationKind.GetAttention)
                notification = notification with {
                    GetAttentionNotification = new (chatId, changeAuthor.Id, entryId?.LocalId ?? 0),
                };
            else if (entryId.HasValue)
                notification = notification with {
                    ChatEntryNotification = new ChatEntryNotificationOption(entryId.Value, changeAuthor.Id),
                };
            else
                notification = notification with {
                    ChatNotification = new ChatNotificationOption(chatId),
                };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken).ConfigureAwait(false);
        }
    }

    private string GetIconUrl(Chat.Chat chat, AuthorFull author)
         => chat.Kind switch {
             ChatKind.Group or ChatKind.Place => chat.Picture?.ContentId.IsNullOrEmpty() == false
                 ? UrlMapper.ContentUrl(chat.Picture.ContentId)
                 : "/favicon.ico",
             ChatKind.Peer => author.Avatar.Media?.ContentId.IsNullOrEmpty() == false
                 ? UrlMapper.ContentUrl(author.Avatar.Media.ContentId)
                 : "/favicon.ico",
             _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
         };

    private static string GetTitle(Chat.Chat chat, AuthorFull author)
        => chat.Kind switch {
            ChatKind.Group or ChatKind.Place => $"{author.Avatar.Name} @ {chat.Title}",
            ChatKind.Peer => $"{author.Avatar.Name}",
            _ => throw new ArgumentOutOfRangeException($"{nameof(chat)}.{nameof(chat.Kind)}", chat.Kind, null),
        };

    private async ValueTask<(string Content, HashSet<MentionId> MentionIds)> GetText(ChatEntry entry, MarkupConsumer consumer, CancellationToken cancellationToken)
    {
        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(entry, consumer, cancellationToken).ConfigureAwait(false);
        var mentionIds = new MentionExtractor().GetMentionIds(markup);
        return (markup.ToReadableText(consumer), mentionIds);
    }

    private static TimeSpan? GetThrottleInterval(Notification notification)
    {
        if (notification.Kind == NotificationKind.Message)
            return Constants.Notification.ThrottleIntervals.Message;
        if (notification.Kind == NotificationKind.Reaction)
            return Constants.Notification.ThrottleIntervals.Message;

        return null;
    }

    private async Task<IReadOnlyList<Device>> ListDevices(UserId userId, Symbol sessionHash, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId)
            .WhereIf(d => d.SessionHash == sessionHash.Value, !sessionHash.IsEmpty)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = dbDevices.Select(d => d.ToModel()).ToList();
        return devices;
    }
}
