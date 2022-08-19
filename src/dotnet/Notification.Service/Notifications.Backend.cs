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

    // [CommandHandler]
    public virtual async Task NotifySubscribers(
        INotificationsBackend.NotifySubscribersCommand notifyCommand,
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
        var userIds = await ListSubscriberIds(chatId, cancellationToken).ConfigureAwait(false);
        var multicastMessage = new MulticastMessage {
            Tokens = null,
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = title,
                Body = textContent,
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
                    Link = OrdinalEquals(_uriMapper.BaseUri.Host, "localhost")
                        ? null
                        : _uriMapper.ToAbsolute($"/chat/{chatId}#{entryId}").ToString(),
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
                var responses = batchResponse.Responses
                    .Zip(deviceGroup)
                    .Select(p => new {
                        p.Second.DeviceId,
                        p.First.IsSuccess,
                        p.First.Exception?.MessagingErrorCode,
                        p.First.Exception?.HttpResponse,
                    })
                    .ToList();
                var responseGroups = responses
                    .GroupBy(x => x.MessagingErrorCode);
                foreach (var responseGroup in responseGroups)
                    if (responseGroup.Key == MessagingErrorCode.Unregistered) {
                        var tokensToRemove = responseGroup
                            .Select(g => g.DeviceId)
                            .ToImmutableArray();
                        _ = _commander.Start(new INotificationsBackend.RemoveDevicesCommand(tokensToRemove), CancellationToken.None);
                    }
                    else if (responseGroup.Key.HasValue) {
                        var firstErrorItem = responseGroup.First();
                        var errorContent = firstErrorItem.HttpResponse == null
                            ? ""
                            : await firstErrorItem.HttpResponse.Content
                                .ReadAsStringAsync(cancellationToken)
                                .ConfigureAwait(false);
                        _log.LogWarning("Notification messages were not sent. ErrorCode = {ErrorCode}; Count = {ErrorCount}; {Details}",
                            responseGroup.Key, responseGroup.Count(), errorContent);
                    }
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

        async Task<string> AuthorNameResolver(string authorId, CancellationToken cancellationToken1)
        {
            var author = await _chatAuthorsBackend.Get(chatId, authorId, true, cancellationToken1).ConfigureAwait(false);
            return author?.Name ?? "";
        }

        async Task<string> UserNameResolver(string userId1, CancellationToken cancellationToken1)
        {
            var author = await _accountsBackend.GetUserAuthor(userId1, cancellationToken1).ConfigureAwait(false);
            return author?.Name ?? "";
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
                .FindAsync(new object?[] { deviceId }, cancellationToken)
                .ConfigureAwait(false);
            if (dbDevice == null)
                continue;

            dbContext.Devices.Remove(dbDevice);
            affectedUserIds.Add(dbDevice.UserId);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(affectedUserIds);
    }
}
