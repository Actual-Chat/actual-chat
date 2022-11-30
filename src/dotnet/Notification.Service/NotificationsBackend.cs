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
            .Where(d => d.UserId == userId)
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

    // [CommandHandler]
    public virtual async Task NotifyUser(INotificationsBackend.NotifyUserCommand command, CancellationToken cancellationToken)
    {
        var (userId, entry) = command;
        var notificationKind = entry.Kind;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invNotificationId = context.Operation().Items.GetOrDefault(Symbol.Empty);
            if (invNotificationId.IsEmpty) // Created
                _ = ListRecentNotificationIds(userId, default);
            else // Updated
                _ = GetNotification(userId, invNotificationId, default);
            return;
        }

        var notificationIds = await ListRecentNotificationIds(userId, cancellationToken).ConfigureAwait(false);
        var notifications = await notificationIds
            .Select(id => GetNotification(userId, id, cancellationToken))
            .Collect()
            .ConfigureAwait(false);

        switch (notificationKind) {
        case NotificationKind.Message: {
            var chatEntryNotification = entry.ChatEntryNotification.Require();
            var existingEntry = notifications
                .OrderByDescending(n => n.NotificationTime)
                .FirstOrDefault(n => n.Kind == notificationKind
                    && n.ChatEntryNotification!.EntryId.ChatId == chatEntryNotification.EntryId.ChatId);

            if (existingEntry != null) {
                var recency = entry.NotificationTime - existingEntry.NotificationTime;
                if (recency <= NotificationConstants.ChatEntryNotificationThrottleInterval)
                    return;

                entry = entry with { Id = existingEntry.Id };
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

        await UpsertEntry(entry, cancellationToken).ConfigureAwait(false);
        await SendSystemNotification(userId, entry, cancellationToken).ConfigureAwait(false);

        async Task UpsertEntry(NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var dbContext = await CreateCommandDbContext(cancellationToken1).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            var dbEntry = await dbContext.Notifications.ForUpdate()
                .SingleOrDefaultAsync(e => e.Id == entry1.Id.Value, cancellationToken)
                .ConfigureAwait(false);

            if (dbEntry != null) {
                dbEntry.Title = entry1.Title;
                dbEntry.Content = entry1.Content;
                dbEntry.IconUrl = entry1.IconUrl;
                dbEntry.ChatId = entry1.ChatId;
                dbEntry.ChatEntryId = entry1.ChatEntryNotification?.EntryId;
                dbEntry.AuthorId = entry1.ChatEntryNotification?.AuthorId;
                dbEntry.ModifiedAt = entry1.NotificationTime;
                dbEntry.HandledAt = null;
                context.Operation().Items.Set(entry1.Id);
            }
            else {
                var now = Clocks.CoarseSystemClock.Now;
                dbEntry = new DbNotification {
                    Id = entry1.Id,
                    UserId = userId,
                    Kind = entry1.Kind,
                    Title = entry1.Title,
                    Content = entry1.Content,
                    IconUrl = entry1.IconUrl,
                    ChatId = entry1.ChatId,
                    ChatEntryId = entry1.ChatEntryNotification?.EntryId,
                    AuthorId = entry1.ChatEntryNotification?.AuthorId,
                    CreatedAt = now,
                    ModifiedAt = now,
                    HandledAt = null,
                };
                dbContext.Notifications.Add(dbEntry);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        async Task SendSystemNotification(UserId userId1, NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var deviceIds = await GetDevicesInternal(userId1, cancellationToken1).ConfigureAwait(false);
            if (deviceIds.Count <= 0)
                return;

            await FirebaseMessagingClient.SendMessage(entry1, deviceIds, cancellationToken1).ConfigureAwait(false);
        }

        async Task<List<Symbol>> GetDevicesInternal(
            UserId userId1,
            CancellationToken cancellationToken1)
        {
            var devices = await ListDevices(userId1, cancellationToken1).ConfigureAwait(false);
            return devices
                .Select(d => d.DeviceId)
                .ToList();
        }
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

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListRecentNotificationIds(UserId userId, CancellationToken cancellationToken)
    {
        // Get notifications for last day
        var yesterdayId = Ulid.NewUlid(Clocks.CoarseSystemClock.UtcNow.AddDays(-1));
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        return dbContext.Notifications
            // ReSharper disable once StringCompareToIsCultureSpecific
            .Where(n => n.UserId == userId.Value && n.Id.CompareTo(yesterdayId.ToString()) > 0)
            .OrderByDescending(n => n.Id)
            .Select(n => (Symbol)n.Id)
            .ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<NotificationEntry> GetNotification(
        UserId userIdId,
        string notificationId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbNotification = await DbNotificationResolver.Get(notificationId, cancellationToken).ConfigureAwait(false);
        if (dbNotification == null)
            throw new InvalidOperationException("Notification doesn't exist.");

        var chatId = new ChatId(dbNotification.ChatId ?? "", ParseOptions.OrNone);
        var chatEntryId = new ChatEntryId(dbNotification.ChatEntryId ?? "", ParseOptions.OrNone);
        var authorId = new AuthorId(dbNotification.AuthorId ?? "", ParseOptions.OrNone);

        return new NotificationEntry(dbNotification.Id,
            dbNotification.Kind,
            dbNotification.Title,
            dbNotification.Content,
            dbNotification.IconUrl,
            dbNotification.ModifiedAt ?? dbNotification.CreatedAt) {
            ChatEntryNotification = dbNotification.Kind switch {
                NotificationKind.Invitation => null,
                NotificationKind.Message => new ChatEntryNotification(chatEntryId, authorId),
                NotificationKind.Reply => new ChatEntryNotification(chatEntryId, authorId),
                NotificationKind.Reaction => new ChatEntryNotification(chatEntryId, authorId),
                _ => throw new ArgumentOutOfRangeException(),
            },
            ChatNotification = dbNotification.Kind switch {
                NotificationKind.Invitation => new ChatNotification(chatId),
                NotificationKind.Message => null,
                NotificationKind.Reply => null,
                NotificationKind.Reaction => null,
                _ => throw new ArgumentOutOfRangeException(),
            },
        };
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
        await EnqueueMessageRelatedNotifications(entry, author, text, NotificationKind.Reaction, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatEntry entry,
        AuthorFull author,
        string text,
        NotificationKind notificationKind,
        IEnumerable<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(entry.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, author);
        var iconUrl = GetIconUrl(chat, author);
        var notificationTime = Clocks.CoarseSystemClock.Now;
        var otherUserIds = author.UserId.IsNone ? userIds : userIds.Where(uid => uid != author.UserId);

        foreach (var otherUserId in otherUserIds) {
            var notificationEntry = new NotificationEntry(
                Ulid.NewUlid().ToString(),
                notificationKind,
                title,
                text,
                iconUrl,
                notificationTime) {
                ChatEntryNotification = new ChatEntryNotification(entry.Id, author.Id),
            };
            await new INotificationsBackend.NotifyUserCommand(otherUserId, notificationEntry)
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
