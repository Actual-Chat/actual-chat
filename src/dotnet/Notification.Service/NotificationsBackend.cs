using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using ActualChat.Commands;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class NotificationsBackend : DbServiceBase<NotificationDbContext>, INotificationsBackend
{
    private readonly IMemoryCache _recentChatsWithNotifications;

    private IAuthorsBackend AuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private IDbEntityResolver<string, DbNotification> DbNotificationResolver { get; }

    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
    private UrlMapper UrlMapper { get; }
    private IUserPresences UserPresences { get; }
    private FirebaseMessagingClient FirebaseMessagingClient { get; }

    public NotificationsBackend(IServiceProvider services) : base(services)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        DbNotificationResolver = services.GetRequiredService<IDbEntityResolver<string, DbNotification>>();

        ChatMarkupHubFactory = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        FirebaseMessagingClient = services.GetRequiredService<FirebaseMessagingClient>();

        _recentChatsWithNotifications = new MemoryCache(new MemoryCacheOptions {
            CompactionPercentage = 0.1,
            SizeLimit = 10_000,
            ExpirationScanFrequency = TimeSpan.FromSeconds(5),
        });
    }

    // [ComputeMethod]
    public virtual async Task<Notification?> Get(
        NotificationId notificationId,
        CancellationToken cancellationToken)
    {
        var dbNotification = await DbNotificationResolver.Get(notificationId, cancellationToken).ConfigureAwait(false);
        return dbNotification?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = dbDevices.Select(d => d.ToModel()).ToList();
        return devices;
    }

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
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
        var dbContext = CreateDbContext();
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
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var notification = command.Notification;
        var kind = notification.Kind;
        var userId = notification.UserId.Require();
        var chatId = notification.ChatId;

        var similar = await Get(notification.Id, cancellationToken).ConfigureAwait(false);
        if (similar != null) {
            var throttleInterval = GetThrottleInterval(notification);
            if (throttleInterval is { } vThrottleInterval) {
                var delta = notification.SentAt - similar.SentAt;
                if (delta <= vThrottleInterval)
                    return;
            }
            notification = notification.WithSimilar(similar);
        }

        var upsertCommand = new NotificationsBackend_Upsert(notification);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        await Send(userId, notification, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUpsert(NotificationsBackend_Upsert command, CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var userId = notification.UserId.Require();

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invIsCreate = context.Operation().Items.GetOrDefault(false);
            if (invIsCreate) // Created
                _ = PseudoListRecentNotificationIds(userId);
            else // Updated
                _ = Get(notification.Id, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbNotification? dbNotification;
        if (notification.Version == 0) { // Create
            notification = notification with {
                Version = VersionGenerator.NextVersion(),
                CreatedAt = notification.CreatedAt == default ? notification.SentAt : notification.CreatedAt,
            };
            dbNotification = new DbNotification();
            dbNotification.UpdateFrom(notification);
            dbContext.Notifications.Add(dbNotification);
            context.Operation().Items.Set(true);
        }
        else { // Update
            var notificationCopy = notification;
            dbNotification = await dbContext.Notifications.ForUpdate()
                .FirstOrDefaultAsync(e => e.Id == notificationCopy.Id, cancellationToken)
                .ConfigureAwait(false);
            dbNotification = dbNotification.RequireVersion(notification.Version);
            notification = notification with {
                Version = VersionGenerator.NextVersion(notification.Version),
            };
            dbNotification.UpdateFrom(notification);
            context.Operation().Items.Set(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveDevices(NotificationsBackend_RemoveDevices removeDevicesCommand, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invUserIds = context.Operation().Items.Get<HashSet<UserId>>();
            if (invUserIds is { Count: > 0 })
                foreach (var invUserId in invUserIds)
                    _ = ListDevices(invUserId, default);
            return;
        }

        var affectedUserIds = new HashSet<UserId>();
        var dbContext = CreateDbContext(readWrite: true);
        await using var __ = dbContext.ConfigureAwait(false);

        foreach (var deviceId in removeDevicesCommand.DeviceIds) {
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
        context.Operation().Items.Set(affectedUserIds);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(NotificationsBackend_RemoveAccount command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var userId = command.UserId;
        var dbContext = CreateDbContext(readWrite: true);
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

    // Event handlers

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(TextEntryChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        var isTranscribedTextEntry = entry.AudioEntryId.HasValue;
        if (isTranscribedTextEntry) {
            if (changeKind != ChangeKind.Update)
                return;
            // When transcribed message is being finalized, it's updated to IsStreaming = false.
            // At this moment we can notify chat users.
        }
        else {
            if (changeKind != ChangeKind.Create)
                return;
            // For regular text messages we notify chat users upon message creation.
        }

        var (text, mentionIds) = await GetText(entry, MarkupConsumer.Notification, cancellationToken).ConfigureAwait(false);
        var chatId = entry.ChatId;
        var key = chatId.Id.Value;
        if (!_recentChatsWithNotifications.TryGetValue(key, out _)) {
            using ICacheEntry cacheEntry = _recentChatsWithNotifications.CreateEntry(key);
            cacheEntry.Size = 1;
            cacheEntry.Value = "";
            cacheEntry.AbsoluteExpirationRelativeToNow = Constants.Notification.ThrottleIntervals.Message;
        }
        else if (mentionIds.Count == 0)
            // throttle low priority notifications
            return;

        var userIds = await ListSubscribedUserIds(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var similarityKey = entry.ChatId;
        await EnqueueMessageRelatedNotifications(
                entry, author, text, NotificationKind.Message, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnReactionChangedEvent(ReactionChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (reaction, entry, author, reactionAuthor, changeKind) = eventCommand;
        if (changeKind == ChangeKind.Remove)
            return;
        if (author.UserId.IsNone) // No notifs to anonymous users
            return;
        if (author.Id == reactionAuthor.Id) // No notifs on your own reactions to your own messages
            return;

        var (text, _) = await GetText(entry, MarkupConsumer.ReactionNotification, cancellationToken).ConfigureAwait(false);
        text = $"{reaction.EmojiId} to \"{text}\"";
        var userIds = new[] { author.UserId };
        var similarityKey = entry.ChatId;
        await EnqueueMessageRelatedNotifications(
                entry, reactionAuthor, text, NotificationKind.Reaction, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // Protected methods

    // [ComputeMethod]
    public virtual Task<Unit> PseudoListRecentNotificationIds(UserId userId)
        => Stl.Async.TaskExt.UnitTask;

    // Private methods

    private async Task Send(UserId userId, Notification notification, CancellationToken cancellationToken1)
    {
        var devices = await ListDevices(userId, cancellationToken1).ConfigureAwait(false);
        if (devices.Count == 0) {
            Log.LogInformation("No recipient devices found found for notification #{NotificationId}", notification.Id);
            return;
        }

        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        await FirebaseMessagingClient.SendMessage(notification, deviceIds, cancellationToken1).ConfigureAwait(false);
    }

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatEntry entry,
        AuthorFull changeAuthor,
        string content,
        NotificationKind kind,
        Symbol similarityKey,
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(entry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, changeAuthor);
        var iconUrl = GetIconUrl(chat, changeAuthor);
        var now = Clocks.CoarseSystemClock.Now;
        var otherUserIds = changeAuthor.UserId.IsNone ? userIds : userIds.Where(uid => uid != changeAuthor.UserId);

        foreach (var otherUserId in otherUserIds) {
            var presence = await UserPresences.Get(otherUserId, cancellationToken).ConfigureAwait(false);
            // Do not send notifications to users who are online
            if (presence is Presence.Online or Presence.Recording)
                continue;
            var notificationId = new NotificationId(otherUserId, kind, similarityKey);
            var notification = new Notification(notificationId) {
                Title = title,
                Content = content,
                IconUrl = iconUrl,
                SentAt = now,
                ChatEntryNotification = new ChatEntryNotificationOption(entry.Id, changeAuthor.Id),
            };
            await new NotificationsBackend_Notify(notification)
                .Enqueue(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private string GetIconUrl(Chat.Chat chat, AuthorFull author)
         => chat.Kind switch {
             ChatKind.Group => chat.Picture?.ContentId.IsNullOrEmpty() == false
                 ? UrlMapper.ContentUrl(chat.Picture.ContentId)
                 : "/favicon.ico",
             ChatKind.Peer => author.Avatar.Media?.ContentId.IsNullOrEmpty() == false
                 ? UrlMapper.ContentUrl(author.Avatar.Media.ContentId)
                 : "/favicon.ico",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.Kind), chat.Kind, null),
         };

    private string GetTitle(Chat.Chat chat, AuthorFull author)
        => chat.Kind switch {
            ChatKind.Group => $"{author.Avatar.Name} @ {chat.Title}",
            ChatKind.Peer => $"{author.Avatar.Name}",
            _ => throw new ArgumentOutOfRangeException(nameof(chat.Kind), chat.Kind, null)
        };

    private async ValueTask<(string Content, HashSet<MentionId> MentionIds)> GetText(ChatEntry entry, MarkupConsumer consumer, CancellationToken cancellationToken)
    {
        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(entry, consumer, cancellationToken).ConfigureAwait(false);
        var mentionIds = new MentionExtractor().GetMentionIds(markup);
        return (markup.ToReadableText(consumer), mentionIds);
    }

    private TimeSpan? GetThrottleInterval(Notification notification)
    {
        if (notification.Kind == NotificationKind.Message)
            return Constants.Notification.ThrottleIntervals.Message;
        if (notification.Kind == NotificationKind.Reaction)
            return Constants.Notification.ThrottleIntervals.Message;

        return null;
    }
}
