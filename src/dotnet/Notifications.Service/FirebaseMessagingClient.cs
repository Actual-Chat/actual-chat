using FirebaseAdmin.Messaging;

namespace ActualChat.Notifications;

public class FirebaseMessagingClient(
    UrlMapper urlMapper,
    FirebaseMessaging firebaseMessaging,
    ICommander commander,
    ILogger<FirebaseMessagingClient> log)
    : IFirebaseMessagingClient
{
    private UrlMapper UrlMapper { get; } = urlMapper;
    private FirebaseMessaging FirebaseMessaging { get; } = firebaseMessaging;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;
    private ILogger? DebugLog => Log;

    public async Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        var notificationId = notification.Id;
        var kind = notification.Kind;
        var title = notification.Title;
        var content = notification.Text;
        var iconUrl = notification.IconUrl;
        var chatNotification = notification as ChatNotification;
        var chatId = (ChatId?)chatNotification?.ChatId;
        var entryId = (ChatEntryId?)null;
        long lastEntryLocalId = 0;
        switch (notification) {
        case AttentionNotification attention:
            lastEntryLocalId = attention.EntryLid;
            break;
        case ChatEntryRelatedNotification related when related.EntryLid != 0:
            entryId = related.EntryId;
            break;
        case ChatEntryNotification entry:
            entryId = entry.EntryId;
            break;
        }

        var absoluteIconUrl = UrlMapper.ToAbsolute(iconUrl, true);
        var isDev = UrlMapper.IsDevVoxt;

        var isChatRelated = chatId is not null;
        var isEntryRelated = entryId is not null;
        var tag = GetChatTag(notification) ?? "topic";
        var link = isEntryRelated ? UrlMapper.ToAbsolute(Links.Chat(entryId))
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
            { Constants.Notification.MessageDataKeys.Timestamp, ((long)notification.CreatedAt.EpochOffset.TotalMilliseconds).ToString() },
        };
        if (lastEntryLocalId > 0)
            data.Add(Constants.Notification.MessageDataKeys.LastEntryLocalId, lastEntryLocalId.ToString());
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            // We do not specify Notification instance, because we use Data messages to deliver notifications to Android
            // Notification = default,
            Data = data,
            Android = new AndroidConfig {
                // We do not specify Notification instance, because we use Data messages to deliver notifications to Android
                // Notification = default,
                Data = new Dictionary<string, string>() {
                    { Constants.Notification.MessageDataKeys.Title, title },
                    { Constants.Notification.MessageDataKeys.Body, content },
                    { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
                },
                Priority = Priority.High,
                // CollapseKey = default, /* We don't use collapsible messages */
                TimeToLive = TimeSpan.FromDays(10),
            },
            Apns = new ApnsConfig {
                Headers = new Dictionary<string, string>() {
                    ["apns-push-type"] = "alert",
                    ["apns-priority"] = kind == NotificationKind.Attention ? "10" : "5",
                },
                Aps = new Aps {
                    Alert = new ApsAlert {
                        Title = title,
                        Body = content,
                    },
                    // iOS only updates a backgrounded app's icon badge from aps.badge -> always send it.
                    Badge = badgeCount,
                    Sound = kind == NotificationKind.Attention ? "attention_ringtone.caf" : "default",
                    MutableContent = true,
                    ThreadId = tag,
                },
                FcmOptions = new ApnsFcmOptions {
                    ImageUrl = absoluteIconUrl,
                },
            },
            Webpush = new WebpushConfig {
                Notification = new WebpushNotification {
                    Renotify = false,
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
        // Only chat-derived tags are emitted: a client closes every notification sharing a tag,
        // so the non-chat "topic" fallback must never be a dismissal tag.
        var dismissedTags = dismissedNotifications
            .Select(GetChatTag)
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

    // The push tag groups notifications on the device (one banner per chat). Returns null for
    // non-chat notifications so they don't collapse under a shared fallback tag. Parses
    // defensively: a ChatNotification whose similarity key isn't a ChatId yields null.
    private static string? GetChatTag(Notification notification)
        => notification switch {
            ChatEntryRelatedNotification n => n.ChatId.Value,
            ChatEntryNotification n => n.ChatId.Value,
            ChatNotification n when ChatId.TryParse(n.SimilarityKey, out var chatId) => chatId.Value,
            _ => null,
        };

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
            DebugLog.LogInformation("Sent {Successfully}/{Total} messages. Result: '{MessageIds}'",
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
                _ = Commander.Start(new NotificationsBackend_RemoveDevices(tokensToRemove), true, CancellationToken.None);
            }
            else if (responseGroup.Key.HasValue) {
                var firstErrorItem = responseGroup.First();
                var errorContent = firstErrorItem.HttpResponse == null
                    ? ""
                    : await firstErrorItem.HttpResponse.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                Log.LogWarning("Notification messages were not sent. ErrorCode = {ErrorCode}; Count = {ErrorCount}; {Details}",
                    responseGroup.Key, responseGroup.Count(), errorContent);
            }
    }
}
