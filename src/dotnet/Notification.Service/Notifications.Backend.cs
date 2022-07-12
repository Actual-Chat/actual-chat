using System.Text;
using ActualChat;
using ActualChat.Chat;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public partial class Notifications
{
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

        var mutedSubscribersTask = dbContext.MutedChatSubscriptions
            .Where(cs => cs.ChatId == chatId)
            .Select(cs => cs.UserId)
            .ToListAsync(cancellationToken);
        var userIdsTask = _chatAuthorsBackend
            .ListUserIds(chatId, cancellationToken);
        await Task.WhenAll(mutedSubscribersTask, userIdsTask).ConfigureAwait(false);

        var mutedSubscribersList = await mutedSubscribersTask.ConfigureAwait(false);
        var mutedSubscribers = mutedSubscribersList
            .Select(id => (Symbol)id)
            .ToHashSet();

        var userIds = await userIdsTask.ConfigureAwait(false);

        var subscriberIds = userIds.Except(mutedSubscribers);
        return subscriberIds.Select(id => id).ToImmutableArray();
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

        var dbNotification = await dbContext.Notifications.Get(notificationId, cancellationToken);
        if (dbNotification == null)
            throw new InvalidOperationException("Notification doesn't exist.");

        return new NotificationEntry(dbNotification.Id,
            dbNotification.UserId,
            dbNotification.NotificationType,
            dbNotification.Title,
            dbNotification.Content,
            dbNotification.ModifiedAt ?? dbNotification.CreatedAt) {
            Message = dbNotification.NotificationType switch {
                NotificationType.Invitation => null,
                NotificationType.Message => new MessageNotificationEntry(dbNotification.ChatId!, dbNotification.ChatEntryId!.Value, dbNotification.ChatUserId!),
                NotificationType.Reply => new MessageNotificationEntry(dbNotification.ChatId!, dbNotification.ChatEntryId!.Value, dbNotification.ChatUserId!),
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

    // [CommandHandler]
    public virtual async Task NotifyNewChatEntry(
        INotificationsBackend.NotifyNewChatEntryCommand notifyCommand,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (chatId, entryId, userId, title, content) = notifyCommand;
        var markupToTextConverter = new MarkupToTextConverter(AuthorNameResolver, UserNameResolver, 100);
        var textContent = await markupToTextConverter.Apply(
            MarkupParser.ParseRaw(content),
            cancellationToken
            ).ConfigureAwait(false);
        var notificationTime = DateTime.UtcNow;
        var userIds = await ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var userIdGroup in userIds.Where(uid => !OrdinalEquals(uid, userId)).Chunk(200))
            await Task.WhenAll(userIdGroup.Select(uid
                    => _commander.Call(
                        new NotifyUserCommand(
                            new NotificationEntry(
                                Ulid.NewUlid().ToString(),
                                uid,
                                NotificationType.Message,
                                title,
                                textContent,
                                notificationTime) {
                                Message = new MessageNotificationEntry(chatId, entryId, userId),
                            }),
                        cancellationToken)))
                .ConfigureAwait(false);

        async Task<string> AuthorNameResolver(string authorId)
        {
            var author = await _chatAuthorsBackend.Get(chatId, authorId, true, cancellationToken).ConfigureAwait(false);
            return author?.Name ?? "";
        }

        async Task<string> UserNameResolver(string userId1)
        {
            var author = await _accountsBackend.GetUserAuthor(userId1, cancellationToken).ConfigureAwait(false);
            return author?.Name ?? "";
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
        var dbContext = _dbContextFactory.CreateDbContext().ReadWrite();
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
        context.Operation().Items.Set(affectedUserIds);
    }

    [CommandHandler]
    protected virtual async Task NotifyUser(NotifyUserCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var entry = command.Entry;
        var (_, userId, notificationType, _, _, _) = entry;
        if (Computed.IsInvalidating()) {
            var invNotificationId = context.Operation().Items.Get<string>();
            if (invNotificationId.IsNullOrEmpty())
                _ = ListRecentNotificationIds(userId, default);
            else
                _ = GetNotification(userId, invNotificationId, default);
            return;
        }

        var notificationIds = await ListRecentNotificationIds(userId, cancellationToken).ConfigureAwait(false);
        foreach (var existingId in notificationIds) {
            var existingEntry = await GetNotification(userId, existingId, cancellationToken).ConfigureAwait(false);
            if (existingEntry.Type != notificationType)
                continue;

            if (notificationType == NotificationType.Message) {
                var messageDetails = entry.Message;
                var existingMessageDetails = existingEntry.Message;
                if (messageDetails == null || existingMessageDetails == null)
                    continue;

                var chatId = messageDetails.ChatId;
                var existingChatId = existingMessageDetails.ChatId;
                if (chatId != existingChatId)
                    continue;

                entry = entry with { NotificationId = existingEntry.NotificationId };
                break;
            }
            if (notificationType is NotificationType.Reply or NotificationType.Invitation)
                continue;

            throw new ArgumentOutOfRangeException();
        }

        await UpsertEntry(entry, cancellationToken).ConfigureAwait(false);
        await SendSystemNotification(entry, cancellationToken);

        async Task UpsertEntry(NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var dbContext = _dbContextFactory.CreateDbContext().ReadWrite();
            await using var __ = dbContext.ConfigureAwait(false);

            var existingEntry = await dbContext.Notifications.Get(entry1.NotificationId, cancellationToken1)
                .ConfigureAwait(false);

            if (existingEntry != null) {
                existingEntry.Title = entry1.Title;
                existingEntry.Content = entry1.Content;
                existingEntry.ChatEntryId = entry1.Message?.EntryId;
                existingEntry.ChatUserId = entry1.Message?.UserId;
                existingEntry.ModifiedAt = entry1.NotificationTime;
                existingEntry.HandledAt = null;
                existingEntry.ModifiedAt = null;
                context.Operation().Items.Set(entry1.NotificationId);
            }
            else {
                existingEntry = new DbNotification {
                    Id = entry1.NotificationId,
                    UserId = entry1.UserId,
                    NotificationType = entry1.Type,
                    Title = entry1.Title,
                    Content = entry1.Content,
                    ChatId = entry1.Message?.ChatId ?? entry1.Chat?.ChatId,
                    ChatEntryId = entry1.Message?.EntryId,
                    ChatUserId = entry1.Message?.UserId,
                    CreatedAt = _clocks.CoarseSystemClock.Now,
                    HandledAt = null,
                    ModifiedAt = null,
                };
                dbContext.Notifications.Add(existingEntry);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        async Task SendSystemNotification(NotificationEntry entry1, CancellationToken cancellationToken1)
        {
            var deviceIds = await GetDevicesInternal(entry1.UserId, cancellationToken1).ConfigureAwait(false);
            if (deviceIds.Count <= 0)
                return;

            await _firebaseMessagingClient.SendMessage(entry1, deviceIds, cancellationToken1).ConfigureAwait(false);
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

    [DataContract]
    public sealed record NotifyUserCommand(
        [property: DataMember] NotificationEntry Entry
    ) : ICommand<Unit>, IBackendCommand;
}
