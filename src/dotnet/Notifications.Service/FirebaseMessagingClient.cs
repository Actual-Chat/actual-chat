using System.Text;
using ActualLab.Diagnostics;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notifications;

public class FirebaseMessagingClient(
    UrlMapper urlMapper,
    FirebaseMessaging firebaseMessaging,
    ICommander commander,
    ILogger<FirebaseMessagingClient> log)
    : IFirebaseMessagingClient
{
    private const int MaxFcmPayloadBytes = 4096;
    private const int FcmPayloadMargin = 512;

    private UrlMapper UrlMapper { get; } = urlMapper;
    private FirebaseMessaging FirebaseMessaging { get; } = firebaseMessaging;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Notifications);

    public async Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        bool isSilent,
        CancellationToken cancellationToken)
    {
        var notificationId = notification.Id;
        var kind = notification.Kind;
        var title = notification.Title;
        var content = notification.Text;
        var iconUrl = notification.IconUrl;
        var chatNotification = notification as ChatNotification;
        var chatId = chatNotification?.ChatId;
        // entryId carries the latest entry (client read-skip), linkEntryId the first-unread tap target.
        var entryId = (ChatEntryId?)null;
        var linkEntryId = (ChatEntryId?)null;
        long lastEntryLocalId = 0;
        switch (notification) {
        case AttentionNotification attention:
            lastEntryLocalId = attention.EntryLid;
            break;
        case ChatEntryRelatedNotification related when related.EntryLid != 0:
            entryId = related.EntryId;
            linkEntryId = related.StartEntryId;
            break;
        case ChatEntryNotification entry:
            entryId = entry.EntryId;
            linkEntryId = entry.EntryId;
            break;
        case ConversationNotification conversation:
            entryId = ChatEntryId.New(conversation.ChatId, conversation.StartEntryLid);
            linkEntryId = entryId;
            break;
        }

        var absoluteIconUrl = UrlMapper.ToAbsolute(iconUrl, true);
        var isDev = UrlMapper.IsDevVoxt;
        // Attention pings and incoming calls ring with elevated priority + a ringtone.
        var isRinger = kind is NotificationKind.Attention or NotificationKind.IncomingCall;

        var isChatRelated = chatId is not null;
        var isEntryRelated = linkEntryId is not null;
        var tag = notification.GetPushTag() ?? "topic";
        // iOS stacks same-thread banners under one group; mentions keep their own banner (tag)
        // but still stack with the rest of their chat.
        var threadTag = notification.GetChatTag() ?? tag;
        var link = isEntryRelated ? UrlMapper.ToAbsolute(Links.Chat(linkEntryId))
            : isChatRelated ? UrlMapper.ToAbsolute(Links.Chat(chatId!))
            : "";

        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.NotificationId, notificationId.Value },
            { Constants.Notification.MessageDataKeys.Tag, tag },
            { Constants.Notification.MessageDataKeys.ChatId, chatId?.Value ?? "" },
            { Constants.Notification.MessageDataKeys.ChatEntryId, entryId?.Value ?? "" },
            { Constants.Notification.MessageDataKeys.Icon, absoluteIconUrl },
            { Constants.Notification.MessageDataKeys.Kind, kind.ToString() },
            { Constants.Notification.MessageDataKeys.Link, link },
            { Constants.Notification.MessageDataKeys.Silent, isSilent.ToString() },
            {
                Constants.Notification.MessageDataKeys.Timestamp,
                ((long)notification.CreatedAt.EpochOffset.TotalMilliseconds).ToString()
            },
        };
        if (lastEntryLocalId > 0)
            data.Add(Constants.Notification.MessageDataKeys.LastEntryLocalId, lastEntryLocalId.ToString());
        var androidData = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.Title, title },
            { Constants.Notification.MessageDataKeys.Body, content },
            { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
        };
        if (notification is ChatEntryRelatedNotification { RecentMessages.Count: > 0 } coalesced) {
            // FCM rejects messages over 4KB, which would drop the push entirely — so the transcript
            // key gets only the space the other data values leave, and is omitted when even one
            // message can't fit (the Body fallback still renders).
            var usedBytes = data.Concat(androidData)
                .Sum(kv => Encoding.UTF8.GetByteCount(kv.Key) + Encoding.UTF8.GetByteCount(kv.Value));
            var budget = Math.Min(PushMessage.MaxJsonLength, MaxFcmPayloadBytes - FcmPayloadMargin - usedBytes);
            var json = PushMessage.ToJson(coalesced.RecentMessages, budget);
            if (!json.IsNullOrEmpty())
                androidData.Add(Constants.Notification.MessageDataKeys.Messages, json);
        }
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            // We do not specify Notification instance, because we use Data messages
            // to deliver notifications to Android
            // Notification = default,
            Data = data,
            Android = new AndroidConfig {
                // We do not specify Notification instance, because we use Data messages
                // to deliver notifications to Android
                // Notification = default,
                Data = androidData,
                Priority = Priority.High,
                // CollapseKey = default, /* We don't use collapsible messages */
                TimeToLive = TimeSpan.FromDays(10),
            },
            Apns = new ApnsConfig {
                Headers = new Dictionary<string, string>() {
                    ["apns-push-type"] = "alert",
                    ["apns-priority"] = isRinger ? "10" : "5",
                    // Coalesce updates for the same banner instead of stacking.
                    ["apns-collapse-id"] = tag,
                },
                Aps = new Aps {
                    Alert = new ApsAlert {
                        Title = title,
                        Body = content,
                    },
                    // iOS only updates a backgrounded app's icon badge from aps.badge -> always send it.
                    Badge = badgeCount,
                    // A silent update refreshes the banner content without playing a sound.
                    Sound = isSilent ? null : isRinger ? "attention_ringtone.caf" : "default",
                    MutableContent = true,
                    ThreadId = threadTag,
                },
                FcmOptions = new ApnsFcmOptions {
                    ImageUrl = absoluteIconUrl,
                },
            },
            Webpush = new WebpushConfig {
                Notification = new WebpushNotification {
                    Renotify = !isSilent,
                    Title = title,
                    Body = content,
                    Tag = tag,
                    RequireInteraction = false,
                    Icon = absoluteIconUrl,
                },
                FcmOptions = new WebpushFcmOptions {
                    Link = UrlMapper.BaseUri.Host == "localhost"
                        ? null
                        : link,
                },
            },
        };
        if (isDev || enableDataCollection.GetValueOrDefault())
            multicastMessage.Android.FcmOptions = new AndroidFcmOptions {
                AnalyticsLabel = "dev_test" // Add label to see data messages statistics in Message delivery reports.
            };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendDismissal(
        IReadOnlyCollection<Notification> dismissedNotifications,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        var dismissedIds = dismissedNotifications.Select(n => n.Id.Value);
        // Only chat/entry-derived tags are emitted: a client closes every notification sharing
        // a tag, so the non-chat "topic" fallback must never be a dismissal tag.
        var dismissedTags = dismissedNotifications
            .Select(NotificationExt.GetPushTag)
            .Where(tag => tag is not null)
            .Distinct();
        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.DismissedIds, string.Join(',', dismissedIds) },
            { Constants.Notification.MessageDataKeys.DismissedTags, string.Join(',', dismissedTags) },
        };
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            Data = data,
            Android = new AndroidConfig {
                Data = data,
                Priority = Priority.High,
                TimeToLive = TimeSpan.FromDays(1),
            },
            Apns = new ApnsConfig {
                Headers = new Dictionary<string, string>() {
                    // A silent push: it only updates the badge and lets the app drop dismissed notifications.
                    ["apns-push-type"] = "background",
                    ["apns-priority"] = "5",
                },
                Aps = new Aps {
                    ContentAvailable = true,
                    Badge = badgeCount,
                },
            },
        };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.SpeechStarted.ToString() },
            { Constants.Notification.MessageDataKeys.ChatId, chatId.Value },
            { Constants.Notification.MessageDataKeys.AuthorId, authorId.Value },
            {
                Constants.Notification.MessageDataKeys.Timestamp,
                ((long)startedAt.EpochOffset.TotalMilliseconds).ToString()
            },
        };
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            Data = data,
            // Android-only data message: a wake for stale speech is useless, so the short
            // TTL + per-chat collapse key keep at most the latest wake queued per device.
            Android = new AndroidConfig {
                Data = data,
                Priority = Priority.High,
                TimeToLive = TimeSpan.FromSeconds(60),
                CollapseKey = $"speech-started-{chatId.Value}",
            },
        };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        Log.LogInformation(
            "SpeechStarted wake for chat '{ChatId}': FCM accepted {SuccessCount}/{Total}",
            chatId, batchResponse.SuccessCount, deviceIds.Count);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleBatchResponse(
        BatchResponse batchResponse,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (DebugLog != null) {
            var messageIds = string.Join(", ",
                batchResponse.Responses.Select(c =>
                    c.IsSuccess
                        ? c.MessageId
                        : c.Exception.MessagingErrorCode.HasValue
                            ? "errCode=" + c.Exception.MessagingErrorCode
                            : c.Exception.Message));
            DebugLog.LogDebug("Sent {Successfully}/{Total} messages. Result: '{MessageIds}'",
                batchResponse.SuccessCount, batchResponse.Responses.Count, messageIds);
        }

        if (batchResponse.FailureCount <= 0)
            return;

        var responses = batchResponse.Responses
            .Zip(deviceIds)
            .Select(p => new {
                DeviceId = p.Second,
                p.First.IsSuccess,
                p.First.Exception?.MessagingErrorCode,
                p.First.Exception?.HttpResponse,
            })
            .ToList();
        var responseGroups = responses
            .GroupBy(x => x.MessagingErrorCode);
        foreach (var responseGroup in responseGroups)
            if (responseGroup.Key is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch) {
                var tokensToRemove = responseGroup
                    .Select(g => g.DeviceId)
                    .ToArray();
                _ = Commander.Start(
                    new NotificationsBackend_RemoveDevices(tokensToRemove), true, CancellationToken.None);
            }
            else if (responseGroup.Key.HasValue) {
                var firstErrorItem = responseGroup.First();
                var errorContent = firstErrorItem.HttpResponse == null
                    ? ""
                    : await firstErrorItem.HttpResponse.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                Log.LogWarning("Notification messages were not sent. ErrorCode = {ErrorCode}; "
                    + "Count = {ErrorCount}; {Details}",
                    responseGroup.Key, responseGroup.Count(), errorContent);
            }
    }
}
