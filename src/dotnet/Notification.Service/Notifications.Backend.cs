using System.Text;
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

    // [CommandHandler]
    public virtual async Task NotifySubscribers(
        INotificationsBackend.NotifySubscribersCommand notifyCommand,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (chatId, entryId, userId, title, content) = notifyCommand;
        var userIds = await ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
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
                    Renotify = false,
                    Tag = chatId,
                    RequireInteraction = false,
                    // Icon = ??? TODO(AK): Set icon
                },
                FcmOptions = new WebpushFcmOptions {
                    // Link = ??? TODO(AK): Set anchor to open particular entry
                    Link = OrdinalEquals(_uriMapper.BaseUri.Host, "localhost")
                        ? null
                        : _uriMapper.ToAbsolute($"/chat/{chatId}").ToString(),
                }
            },
        };
        var deviceIdGroups = userIds
            .Where(uid => !OrdinalEquals(uid, userId))
            .ToAsyncEnumerable()
            .SelectMany(uid => GetDevicesInternal(uid, cancellationToken))
            .Chunk(200, cancellationToken);

        await foreach (var deviceGroup in deviceIdGroups.ConfigureAwait(false)) {
            multicastMessage.Tokens = deviceGroup.Select(p => p.DeviceId).ToList();
            var batchResponse = await _firebaseMessaging.SendMulticastAsync(multicastMessage, cancellationToken)
                .ConfigureAwait(false);

            if (batchResponse.FailureCount > 0) {
                var errorInfoItems = await Task.WhenAll(batchResponse.Responses
                    .Where(r => !r.IsSuccess)
                    .Select(async r => new {
                        r.Exception.MessagingErrorCode,
                        r.Exception.HttpResponse.StatusCode,
                        Content = await r.Exception.HttpResponse.Content.ReadAsStringAsync(cancellationToken)
                            .ConfigureAwait(false),
                    })).ConfigureAwait(false);
                var errorDetails = errorInfoItems
                    .Select(x => $"MessagingError = {x.MessagingErrorCode}; HttpCode = {x.StatusCode}; Content = {x.Content}")
                    .Aggregate(new StringBuilder(), (sb, item) => sb.AppendLine(item))
                    .ToString();
                _log.LogWarning("Notification messages were not sent. NotificationCount = {NotificationCount}; {Details}",
                    batchResponse.FailureCount, errorDetails);
            }

            if (batchResponse.SuccessCount > 0)
                _ = Task.Run(
                    () => PersistMessages(chatId,
                        entryId,
                        deviceGroup,
                        batchResponse.Responses,
                        cancellationToken),
                    cancellationToken);
        }

        async IAsyncEnumerable<(string DeviceId, string UserId)> GetDevicesInternal(
            string userId1,
            [EnumeratorCancellation] CancellationToken cancellationToken1)
        {
            var devices = await ListDevices(userId1, cancellationToken1).ConfigureAwait(false);
            foreach (var device in devices)
                yield return (device.DeviceId, userId1);
        }

        async Task PersistMessages(
            string chatId1,
            long entryId1,
            IReadOnlyList<(string DeviceId, string UserId)> devices,
            IReadOnlyList<SendResponse> responses,
            CancellationToken cancellationToken1)
        {
            // TODO(AK): sharding by userId - code is running at a sharded service already
            var dbContext = _dbContextFactory.CreateDbContext().ReadWrite();
            await using var __ = dbContext.ConfigureAwait(false);

            var dbMessages = responses
                .Zip(devices)
                .Where(pair => pair.First.IsSuccess)
                .Select(pair => new DbMessage {
                    Id = pair.First.MessageId,
                    DeviceId = pair.Second.DeviceId,
                    ChatId = chatId1,
                    ChatEntryId = entryId1,
                    CreatedAt = _clocks.SystemClock.Now,
                });

            await dbContext.AddRangeAsync(dbMessages, cancellationToken1).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
