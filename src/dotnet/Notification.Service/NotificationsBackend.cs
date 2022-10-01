using ActualChat.Chat;
using ActualChat.Chat.Events;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using ActualChat.Commands;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class NotificationsBackend : DbServiceBase<NotificationDbContext>, INotificationsBackend
{
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IDbEntityResolver<string, DbNotification> DbNotificationResolver { get; }
    private FirebaseMessagingClient FirebaseMessagingClient { get; }
    private UrlMapper UrlMapper { get; }

    public NotificationsBackend(
        IServiceProvider services,
        IChatAuthorsBackend chatAuthorsBackend,
        FirebaseMessagingClient firebaseMessagingClient,
        IChatsBackend chatsBackend,
        IDbEntityResolver<string, DbNotification> dbNotificationResolver,
        UrlMapper urlMapper) : base(services)
    {
        ChatAuthorsBackend = chatAuthorsBackend;
        FirebaseMessagingClient = firebaseMessagingClient;
        ChatsBackend = chatsBackend;
        DbNotificationResolver = dbNotificationResolver;
        UrlMapper = urlMapper;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Device>> ListDevices(string userId, CancellationToken cancellationToken)
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
    public virtual async Task<ImmutableArray<Symbol>> ListSubscriberIds(string chatId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var mutedUserIdsTask = dbContext.MutedChatSubscriptions
            .Where(cs => cs.ChatId == chatId)
            .Select(cs => cs.UserId)
            .ToListAsync(cancellationToken);
        var userIdsTask = ChatAuthorsBackend
            .ListUserIds(chatId, cancellationToken);
        await Task.WhenAll(mutedUserIdsTask, userIdsTask).ConfigureAwait(false);

        var mutedUserIds = (await mutedUserIdsTask.ConfigureAwait(false))
            .Select(userId => (Symbol)userId)
            .ToHashSet();
        var userIds = await userIdsTask.ConfigureAwait(false);
        var subscriberIds = userIds
            .Where(userId => !mutedUserIds.Contains(userId))
            .ToImmutableArray();
        return subscriberIds;
    }

    // [CommandHandler]
    public virtual async Task NotifyUser(INotificationsBackend.NotifyUserCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var (userId, entry) = command;
        var notificationType = entry.Type;
        if (Computed.IsInvalidating()) {
            var invNotificationId = context.Operation().Items.Get<string>();
            if (invNotificationId.IsNullOrEmpty())
                _ = ListRecentNotificationIds(userId, default);
            else
                _ = GetNotification(userId, invNotificationId, default);
            return;
        }

        var notificationIds = await ListRecentNotificationIds(userId, cancellationToken).ConfigureAwait(false);
        var notifications = await notificationIds
            .Select(id => GetNotification(userId, id, cancellationToken))
            .Collect()
            .ConfigureAwait(false);

        if (notificationType == NotificationType.Message) {
            var messageDetails = entry.Message;
            var existingEntry = notifications
                .OrderByDescending(n => n.NotificationTime)
                .FirstOrDefault(n => n.Type == notificationType
                    && OrdinalEquals(n.Message?.ChatId, messageDetails?.ChatId));

            if (existingEntry != null) {
                // throttle message notifications
                if (entry.NotificationTime - existingEntry.NotificationTime <= TimeSpan.FromMinutes(5))
                    return;

                entry = entry with { NotificationId = existingEntry.NotificationId };
            }
        }
        else if (notificationType == NotificationType.Invitation) {

        }
        else if (notificationType == NotificationType.Reply) {

        }
        else if (notificationType == NotificationType.Mention) {

        }
        else
            throw StandardError.NotSupported<NotificationType>("Notification type is unsupported.");


        await UpsertEntry(entry, cancellationToken).ConfigureAwait(false);
        await SendSystemNotification(userId, entry, cancellationToken).ConfigureAwait(false);

        async Task UpsertEntry(NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var dbContext = CreateDbContext().ReadWrite();
            await using var __ = dbContext.ConfigureAwait(false);

            var existingEntry = await dbContext.Notifications.Get(entry1.NotificationId, cancellationToken1)
                .ConfigureAwait(false);

            if (existingEntry != null) {
                existingEntry.Title = entry1.Title;
                existingEntry.Content = entry1.Content;
                existingEntry.IconUrl = entry1.IconUrl;
                existingEntry.ChatEntryId = entry1.Message?.EntryId;
                existingEntry.ChatAuthorId = entry1.Message?.AuthorId;
                existingEntry.ModifiedAt = entry1.NotificationTime;
                existingEntry.HandledAt = null;
                existingEntry.ModifiedAt = null;
                context.Operation().Items.Set(entry1.NotificationId);
            }
            else {
                existingEntry = new DbNotification {
                    Id = entry1.NotificationId,
                    UserId = userId,
                    NotificationType = entry1.Type,
                    Title = entry1.Title,
                    Content = entry1.Content,
                    IconUrl = entry1.IconUrl,
                    ChatId = entry1.Message?.ChatId ?? entry1.Chat?.ChatId,
                    ChatEntryId = entry1.Message?.EntryId,
                    ChatAuthorId = entry1.Message?.AuthorId,
                    CreatedAt = Clocks.CoarseSystemClock.Now,
                    HandledAt = null,
                    ModifiedAt = null,
                };
                dbContext.Notifications.Add(existingEntry);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        async Task SendSystemNotification(string userId1, NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var deviceIds = await GetDevicesInternal(userId1, cancellationToken1).ConfigureAwait(false);
            if (deviceIds.Count <= 0)
                return;

            await FirebaseMessagingClient.SendMessage(entry1, deviceIds, cancellationToken1).ConfigureAwait(false);
        }

        async Task<List<string>> GetDevicesInternal(
            string userId1,
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
            var invUserIds = context.Operation().Items.Get<HashSet<string>>();
            if (invUserIds is { Count: > 0 })
                foreach (var invUserId in invUserIds)
                    _ = ListDevices(invUserId, default);
            return;
        }
        var affectedUserIds = new HashSet<string>(StringComparer.Ordinal);
        var dbContext = CreateDbContext(readWrite: true);
        await using var __ = dbContext.ConfigureAwait(false);

        foreach (var deviceId in removeDevicesCommand.DeviceIds) {
            var dbDevice = await dbContext.Devices
                .Get(deviceId, cancellationToken)
                .ConfigureAwait(false);
            if (dbDevice == null)
                continue;

            dbContext.Devices.Remove(dbDevice);
            affectedUserIds.Add(dbDevice.UserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", affectedUserIds.Count);
        context.Operation().Items.Set(affectedUserIds);
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<string>> ListRecentNotificationIds(string userId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        return dbContext.Notifications
            .OrderByDescending(n => n.Id)
            .Take(20)
            .Select(n => n.Id)
            .ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<NotificationEntry> GetNotification(
        string userId,
        string notificationId,
        CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbNotification = await DbNotificationResolver.Get(notificationId, cancellationToken).ConfigureAwait(false);
        if (dbNotification == null)
            throw new InvalidOperationException("Notification doesn't exist.");

        return new NotificationEntry(dbNotification.Id,
            dbNotification.NotificationType,
            dbNotification.Title,
            dbNotification.Content,
            dbNotification.IconUrl,
            dbNotification.ModifiedAt ?? dbNotification.CreatedAt) {
            Message = dbNotification.NotificationType switch {
                NotificationType.Invitation => null,
                NotificationType.Message => new MessageNotificationEntry(dbNotification.ChatId!, dbNotification.ChatEntryId!.Value, dbNotification.ChatAuthorId!),
                NotificationType.Reply => new MessageNotificationEntry(dbNotification.ChatId!, dbNotification.ChatEntryId!.Value, dbNotification.ChatAuthorId!),
                _ => throw new ArgumentOutOfRangeException(),
            },
            Chat = dbNotification.NotificationType switch {
                NotificationType.Invitation => new ChatNotificationEntry(dbNotification.ChatId!),
                NotificationType.Message => null,
                NotificationType.Reply => null,
                _ => throw new ArgumentOutOfRangeException(),
            }
        };
    }

    // Event handlers

    [EventHandler]
    public virtual async Task OnTextEntryChangedEvent(
        TextEntryChangedEvent @event,
        CancellationToken cancellationToken)
    {
        var (chatId, entryId, authorId, content, state) = @event;
        if (state == EntryState.Removed)
            return;

        if (Computed.IsInvalidating())
            return;

        var chatAuthor = await ChatAuthorsBackend.Get(chatId, authorId, true, cancellationToken).ConfigureAwait(false);
        var userId = chatAuthor!.UserId;
        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var title = GetTitle(chat, chatAuthor);
        var iconUrl = GetIconUrl(chat, chatAuthor);
        var textContent = GetContent(content);
        var notificationTime = Clocks.CoarseSystemClock.Now;
        var userIds = await ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var uid in userIds.Where(uid => !OrdinalEquals(uid, userId)))
            await new INotificationsBackend.NotifyUserCommand(
                    uid,
                    new NotificationEntry(
                        Ulid.NewUlid().ToString(),
                        NotificationType.Message,
                        title,
                        textContent,
                        iconUrl,
                        notificationTime) {
                        Message = new MessageNotificationEntry(chatId, entryId, authorId),
                    })
                .Enqueue(Queues.Users.ShardBy(uid), cancellationToken)
                .ConfigureAwait(false);
    }

    // Private methods

    private string GetIconUrl(Chat.Chat chat, ChatAuthor chatAuthor)
         => chat.ChatType switch {
             ChatType.Group => !chat.Picture.IsNullOrEmpty() ? UrlMapper.ContentUrl(chat.Picture) : "/favicon.ico",
             ChatType.Peer => !chatAuthor.Picture.IsNullOrEmpty() ? UrlMapper.ContentUrl(chatAuthor.Picture) : "/favicon.ico",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null),
         };

    private static string GetTitle(Chat.Chat chat, ChatAuthor chatAuthor)
         => chat.ChatType switch {
             ChatType.Group => $"{chatAuthor.Name} @ {chat.Title}",
             ChatType.Peer => $"{chatAuthor.Name}",
             _ => throw new ArgumentOutOfRangeException(nameof(chat.ChatType), chat.ChatType, null)
         };

     private static string GetContent(string chatEventContent)
     {
         var markup = MarkupParser.ParseRaw(chatEventContent);
         markup = new MarkupTrimmer(100).Rewrite(markup);
         return MarkupFormatter.ReadableUnstyled.Format(markup);
     }
}
