using ActualChat.Chat;
using ActualChat.Notification.Backend;
using ActualChat.Notification.Db;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Notification;

public class NotificationsBackend : DbServiceBase<NotificationDbContext>, INotificationsBackend
{
    private IChatAuthorsBackend ChatAuthorsBackend { get; }
    private UriMapper UriMapper { get; }
    private FirebaseMessaging FirebaseMessaging { get; }

    public NotificationsBackend(
        IServiceProvider services,
        IChatAuthorsBackend chatAuthorsBackend,
        UriMapper uriMapper,
        FirebaseMessaging firebaseMessaging) : base(services)
    {
        ChatAuthorsBackend = chatAuthorsBackend;
        UriMapper = uriMapper;
        FirebaseMessaging = firebaseMessaging;
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
    public virtual async Task NotifySubscribers(
        INotificationsBackend.NotifySubscribersCommand notifyCommand,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return;

        var (chatId, entryId, userId, title, iconUrl, content) = notifyCommand;
        var userIds = await ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
        var multicastMessage = new MulticastMessage {
            Tokens = null,
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = title,
                Body = content,
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
                    Icon = iconUrl,
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
                    Title = title,
                    Body = content,
                    Tag = chatId,
                    RequireInteraction = false,
                    Icon = iconUrl,
                },
                FcmOptions = new WebpushFcmOptions {
                    Link = OrdinalEquals(UriMapper.BaseUri.Host, "localhost")
                        ? null
                        : UriMapper.ToAbsolute($"/chat/{chatId}#{entryId}").ToString(),
                },
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["chatId"] = chatId,
                    ["icon"] = iconUrl,
                },
            },
        };
        var deviceIdGroups = ListUserDevicePairs(userIds, userId, cancellationToken)
            .Chunk(200, cancellationToken)
            .Buffer(2, cancellationToken);

        await foreach (var deviceGroup in deviceIdGroups.ConfigureAwait(false)) {
            multicastMessage.Tokens = deviceGroup.Select(p => p.DeviceId).ToList();
            var batchResponse = await FirebaseMessaging
                .SendMulticastAsync(multicastMessage, cancellationToken)
                .ConfigureAwait(false);

            Log.LogInformation(
                "NotifySubscribers: notification batch is sent for "
                + "chat #{ChatId}, entry #{ChatEntryId} to {RecipientsCount} device(s) "
                + "({SuccessCount} ok, {FailureCount} failures)",
                chatId,
                entryId,
                deviceGroup.Count,
                batchResponse.SuccessCount,
                batchResponse.FailureCount);

            if (batchResponse.FailureCount > 0) {
                var responses = batchResponse.Responses
                    .Zip(deviceGroup)
                    .Select(p => new {
                        p.Second.DeviceId,
                        p.First.IsSuccess,
                        p.First.Exception?.MessagingErrorCode,
                        p.First.Exception?.HttpResponse,
                    })
                    .ToList();
                var responseGroups = responses.GroupBy(x => x.MessagingErrorCode);
                foreach (var responseGroup in responseGroups)
                    if (responseGroup.Key == MessagingErrorCode.Unregistered) {
                        var removedDeviceIds = responseGroup
                            .Select(g => g.DeviceId)
                            .ToImmutableArray();
                        _ = Commander.Start(new INotificationsBackend.RemoveDevicesCommand(removedDeviceIds), CancellationToken.None);
                    }
                    else if (responseGroup.Key.HasValue) {
                        var firstErrorItem = responseGroup.First();
                        var errorContent = firstErrorItem.HttpResponse == null
                            ? ""
                            : await firstErrorItem.HttpResponse.Content
                                .ReadAsStringAsync(cancellationToken)
                                .ConfigureAwait(false);
                        Log.LogWarning("NotifySubscribers: notifications failed, "
                            + "ErrorCode = {ErrorCode} (x {ErrorCount}), Details: {Details}",
                            responseGroup.Key, responseGroup.Count(), errorContent);
                    }
            }

            if (batchResponse.SuccessCount > 0)
                _ = BackgroundTask.Run(
                    () => PersistMessages(chatId, entryId, deviceGroup, batchResponse.Responses, cancellationToken),
                    Log, "PersistMessages failed",
                    cancellationToken);
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
                .FindAsync(new object?[] { deviceId }, cancellationToken)
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

    // Private methods

    private async IAsyncEnumerable<(Symbol UserId, string DeviceId)> ListUserDevicePairs(
        ImmutableArray<Symbol> userIds,
        Symbol currentUserId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (userIds.Length == 0)
            yield break;

        var filteredUserIds = userIds.Where(userId => userId != currentUserId);
        foreach (var userId in filteredUserIds) {
            var devices = await ListDevices(userId, cancellationToken).ConfigureAwait(false);
            foreach (var device in devices)
                yield return (userId, device.DeviceId);
        }
    }

    private async Task PersistMessages(
        string chatId,
        long chatEntryId,
        IReadOnlyList<(Symbol UserId, string DeviceId)> devices,
        IReadOnlyList<SendResponse> responses,
        CancellationToken cancellationToken)
    {
        // TODO(AK): Sharding by userId - code is running at a sharded service already
        var dbContext = CreateDbContext(readWrite: true);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbMessages = responses
            .Zip(devices)
            .Where(pair => pair.First.IsSuccess)
            .Select(pair => new DbMessage {
                Id = pair.First.MessageId,
                DeviceId = pair.Second.DeviceId,
                ChatId = chatId,
                ChatEntryId = chatEntryId,
                CreatedAt = Clocks.SystemClock.Now,
            });

        await dbContext.AddRangeAsync(dbMessages, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
