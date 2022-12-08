using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using ActualChat.Commands;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class NotificationsBackend : DbServiceBase<NotificationDbContext>, INotificationsBackend
{
    private IAuthorsBackend AuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private IDbEntityResolver<string, DbNotification> DbNotificationResolver { get; }
    private FirebaseMessagingClient FirebaseMessagingClient { get; }
    private UrlMapper UrlMapper { get; }

    public NotificationsBackend(IServiceProvider services) : base(services)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        ServerKvasBackend = services.GetRequiredService<IServerKvasBackend>();
        DbNotificationResolver = services.GetRequiredService<IDbEntityResolver<string, DbNotification>>();
        FirebaseMessagingClient = services.GetRequiredService<FirebaseMessagingClient>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Device>> ListDevices(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = dbDevices.Select(d => d.ToModel()).ToImmutableArray();
        return devices;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserId>> ListSubscribedUserIds(ChatId chatId, CancellationToken cancellationToken)
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
            .ToImmutableArray();
        return subscriberIds;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<NotificationId>> ListRecentNotificationIds(UserId userId, CancellationToken cancellationToken)
    {
        // Get notifications for last day
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var yesterdayId = new NotificationId(userId, Ulid.NewUlid(Clocks.CoarseSystemClock.UtcNow.AddDays(-1)));
        return dbContext.Notifications
            // ReSharper disable once StringCompareToIsCultureSpecific
            .Where(n => n.UserId == userId.Value && n.Id.CompareTo(yesterdayId.ToString()) > 0)
            .OrderByDescending(n => n.Id)
            .Select(n => new NotificationId(n.Id))
            .ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<Notification> Get(
        NotificationId notificationId,
        CancellationToken cancellationToken)
    {
        var dbNotification = await DbNotificationResolver.Get(notificationId, cancellationToken).ConfigureAwait(false);
        return dbNotification.Require().ToModel();
    }

    // [CommandHandler]
    public virtual async Task Notify(INotificationsBackend.NotifyCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var notification = command.Notification;
        var kind = notification.Kind;
        var userId = notification.UserId.Require();
        var chatId = notification.ChatId;

        switch (kind) {
        case NotificationKind.Message: {
            // TODO(AY): Refactor this thing to O(1) DB lookups!
            var notificationIds = await ListRecentNotificationIds(userId, cancellationToken).ConfigureAwait(false);
            var notifications = await notificationIds
                .Select(id => Get(id, cancellationToken))
                .Collect()
                .ConfigureAwait(false);
            var recentSimilarNotification = notifications
                .OrderByDescending(n => n.HandledAt)
                .FirstOrDefault(n => n.Kind == kind && n.ChatId == chatId);

            if (recentSimilarNotification != null) {
                var recency = notification.HandledAt - recentSimilarNotification.HandledAt;
                if (recency <= NotificationConstants.ChatEntryNotificationThrottleInterval)
                    return;

                notification = notification with {
                    Id = recentSimilarNotification.Id,
                    Version = recentSimilarNotification.Version,
                };
            }
            break;
        }
        case NotificationKind.Invitation:
            break;
        case NotificationKind.Reply:
            break;
        case NotificationKind.Mention:
            break;
        case NotificationKind.Reaction:
            break;
        default:
            throw StandardError.NotSupported<NotificationKind>("Notification type is unsupported.");
        }

        var upsertCommand = new INotificationsBackend.UpsertCommand(notification);
        await Commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
        await Send(userId, notification, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Upsert(INotificationsBackend.UpsertCommand command, CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var userId = notification.UserId.Require();

        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invNotificationId = context.Operation().Items.GetOrDefault(NotificationId.None);
            if (invNotificationId.IsNone) // Created
                _ = ListRecentNotificationIds(userId, default);
            else // Updated
                _ = Get(invNotificationId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        DbNotification? dbNotification;
        var now = Clocks.CoarseSystemClock.Now;
        if (notification.Version == 0) {
            notification = notification with {
                Id = notification.Id.Or(userId, static userId1 => new NotificationId(userId1, Ulid.NewUlid())),
                Version = VersionGenerator.NextVersion(),
                CreatedAt = now,
            };
            dbNotification = new DbNotification();
            dbNotification.UpdateFrom(notification);
            dbContext.Notifications.Add(dbNotification);
        }
        else {
            var notificationCopy = notification;
            dbNotification = await dbContext.Notifications.ForUpdate()
                .SingleOrDefaultAsync(e => e.Id == notificationCopy.Id.Value, cancellationToken)
                .ConfigureAwait(false);
            dbNotification = dbNotification.RequireVersion(notification.Version);
            notification = notification with {
                Version = VersionGenerator.NextVersion(notification.Version),
            };
            dbNotification.UpdateFrom(notification);
            context.Operation().Items.Set(notification.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task RemoveDevices(INotificationsBackend.RemoveDevicesCommand removeDevicesCommand, CancellationToken cancellationToken)
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

    // Event handlers

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(
        TextEntryChangedEvent @event,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (entry, author, changeKind) = @event;
        if (changeKind != ChangeKind.Create || entry.IsServiceEntry)
            return;

        var text = GetText(entry);
        var userIds = await ListSubscribedUserIds(entry.ChatId, cancellationToken).ConfigureAwait(false);
        await EnqueueMessageRelatedNotifications(entry, author, text, NotificationKind.Message, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnReactionChangedEvent(
        ReactionChangedEvent @event,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (reaction, entry, author, reactionAuthor, changeKind) = @event;
        if (changeKind == ChangeKind.Remove)
            return;
        if (author.UserId.IsNone) // No notifs to anonymous users
            return;
        if (author.Id == reactionAuthor.Id) // No notifs on your own reactions to your own messages
            return;

        var text = $"{reaction.EmojiId} to \"{GetText(entry, 30)}\"";
        var userIds = new[] { author.UserId };
        await EnqueueMessageRelatedNotifications(entry, reactionAuthor, text, NotificationKind.Reaction, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task Send(UserId userId, Notification notification, CancellationToken cancellationToken1)
    {
        var devices = await ListDevices(userId, cancellationToken1).ConfigureAwait(false);
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        await FirebaseMessagingClient.SendMessage(notification, deviceIds, cancellationToken1).ConfigureAwait(false);
    }

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatEntry entry,
        AuthorFull changeAuthor,
        string content,
        NotificationKind kind,
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(entry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, changeAuthor);
        var iconUrl = GetIconUrl(chat, changeAuthor);
        var createdAt = Clocks.CoarseSystemClock.Now;
        var otherUserIds = changeAuthor.UserId.IsNone ? userIds : userIds.Where(uid => uid != changeAuthor.UserId);

        foreach (var otherUserId in otherUserIds) {
            var notification = new Notification(default) {
                Kind = kind,
                Title = title,
                Content = content,
                IconUrl = iconUrl,
                CreatedAt = createdAt,
                ChatEntryNotification = new ChatEntryNotification(entry.Id, changeAuthor.Id),
            };
            await new INotificationsBackend.NotifyCommand(notification)
                .Enqueue(Queues.Users.ShardBy(otherUserId), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private string GetIconUrl(Chat.Chat chat, AuthorFull author)
         => chat.Kind switch {
             ChatKind.Group => !chat.Picture.IsNullOrEmpty() ? UrlMapper.ContentUrl(chat.Picture) : "/favicon.ico",
             ChatKind.Peer => !author.Avatar.Picture.IsNullOrEmpty()
                 ? UrlMapper.ContentUrl(author.Avatar.Picture)
                 : "/favicon.ico",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.Kind), chat.Kind, null),
         };

    private static string GetTitle(Chat.Chat chat, AuthorFull author)
         => chat.Kind switch {
             ChatKind.Group => $"{author.Avatar.Name} @ {chat.Title}",
             ChatKind.Peer => $"{author.Avatar.Name}",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.Kind), chat.Kind, null)
         };

     private static string GetText(ChatEntry entry, int maxLength = 100)
     {
         var content = entry.GetContentOrDescription();
         var markup = MarkupParser.ParseRaw(content);
         markup = new MarkupTrimmer(maxLength).Rewrite(markup);
         return MarkupFormatter.ReadableUnstyled.Format(markup);
     }
}
