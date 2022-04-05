using ActualChat.Chat;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly FirebaseMessaging _firebaseMessaging;
    private readonly INotificationsBackend _notificationsBackend;
    private readonly IChatAuthorsBackend _chatAuthorsBackend;
    private readonly ILogger<NotificationPublisher> _log;
    private readonly IDbContextFactory<NotificationDbContext> _dbContextFactory;
    private readonly MomentClockSet _clocks;

    public NotificationPublisher(
        FirebaseMessaging firebaseMessaging,
        INotificationsBackend notificationsBackend,
        IChatAuthorsBackend chatAuthorsBackend,
        IDbContextFactory<NotificationDbContext> dbContextFactory,
        MomentClockSet clocks,
        ILogger<NotificationPublisher> log)
    {
        _firebaseMessaging = firebaseMessaging;
        _notificationsBackend = notificationsBackend;
        _chatAuthorsBackend = chatAuthorsBackend;
        _dbContextFactory = dbContextFactory;
        _clocks = clocks;
        _log = log;
    }

    public Task Publish(NotificationEntry notification, CancellationToken cancellationToken)
    {
        switch (notification) {
            case UserNotificationEntry userEntry:
                return PublishUserNotification(userEntry, cancellationToken);
            case ChatNotificationEntry topicEntry:
                return PublishChatNotification(topicEntry, cancellationToken);
            case null:
                throw new ArgumentException("notification should not be null.", nameof(notification));
            default:
                throw new NotSupportedException(notification.GetType().Name + " is not supported.");
        }
    }

    private async Task PublishChatNotification(
        ChatNotificationEntry chatNotificationEntry,
        CancellationToken cancellationToken)
    {
        var (chatId, entryId, authorId, title, content, _) = chatNotificationEntry;
        var userIds = await _notificationsBackend.GetSubscribers(chatId, cancellationToken).ConfigureAwait(false);
        var chatAuthor = await _chatAuthorsBackend.Get(chatId, authorId, false, cancellationToken).ConfigureAwait(false);
        var userId = chatAuthor?.UserId;

        var multicastMessage = new MulticastMessage {
            Tokens = null,
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = title,
                Body = content,
                // ImageUrl = ??? TODO(AK): set image url
            },
            Android = new AndroidConfig {
                Notification = new AndroidNotification {
                    // Color = ??? TODO(AK): set color
                    Priority = NotificationPriority.DEFAULT,
                    // Sound = ??? TODO(AK): set sound
                    Tag = chatId,
                    Visibility = NotificationVisibility.PRIVATE,
                    // ClickAction = ?? TODO(AK): Set click action for Android
                    DefaultSound = true,
                    LocalOnly = false,
                    // NotificationCount = TODO(AK): Set unread message count!
                    // Icon = ??? TODO(AK): Set icon
                },
                Priority = Priority.Normal,
                CollapseKey = "topics",
                // RestrictedPackageName = TODO(AK): Set android package name
                TimeToLive = TimeSpan.FromMinutes(180),
            },
            Apns = new ApnsConfig {
                Aps = new Aps {
                    Sound = "default",
                    MutableContent = true,
                    ThreadId = "topics",
                },
            },
            Webpush = new WebpushConfig {
                Notification = new WebpushNotification {
                    Renotify = true,
                    Tag = chatId,
                    RequireInteraction = false,
                    // Icon = ??? TODO(AK): Set icon
                },
                FcmOptions = new WebpushFcmOptions {
                    // Link = ??? TODO(AK): Set topic Url to be opened with notification click
                }
            },
        };

        var deviceIdGroups = userIds
            .Where(uid => !string.Equals(uid, userId, StringComparison.Ordinal))
            .ToAsyncEnumerable()
            .SelectMany(uid => GetDevices(uid, cancellationToken))
            .Buffer(200, cancellationToken);

        await foreach (var deviceGroup in deviceIdGroups.ConfigureAwait(false)) {
            multicastMessage.Tokens = deviceGroup;
            var batchResponse = await _firebaseMessaging.SendMulticastAsync(multicastMessage, cancellationToken).ConfigureAwait(false);

            if (batchResponse.FailureCount > 0)
                _log.LogWarning("Notification messages were not sent. NotificationCount = {NotificationCount}",
                    batchResponse.FailureCount);

            if (batchResponse.SuccessCount > 0)
                _ = Task.Run(
                    () => PersistMessages(chatId, entryId, deviceGroup, batchResponse.Responses, cancellationToken),
                    cancellationToken);
        }

        async IAsyncEnumerable<string> GetDevices(string userId1, [EnumeratorCancellation] CancellationToken cancellationToken1)
        {
            var devices = await _notificationsBackend.GetDevices(userId1, cancellationToken1).ConfigureAwait(false);
            foreach (var device in devices)
                yield return device.DeviceId;
        }

        async Task PersistMessages(
            string chatId1,
            long entryId1,
            IReadOnlyList<string> tokens,
            IReadOnlyList<SendResponse> responses,
            CancellationToken cancellationToken1)
        {
            var dbContext = _dbContextFactory.CreateDbContext().ReadWrite();
            await using var __ = dbContext.ConfigureAwait(false);

            var dbMessages = responses
                .Zip(tokens)
                .Where(pair => pair.First.IsSuccess)
                .Select(pair => new DbMessage {
                    Id = pair.First.MessageId,
                    DeviceId = pair.Second,
                    ChatId = chatId1,
                    ChatEntryId = entryId1,
                    CreatedAt = _clocks.SystemClock.Now,
                });

            await dbContext.AddRangeAsync(dbMessages, cancellationToken1).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task PublishUserNotification(
        UserNotificationEntry userNotificationEntry,
        CancellationToken cancellationToken)
    {
        var userId = userNotificationEntry.UserId;

        throw new NotImplementedException();
    }
}
