using FirebaseAdmin.Messaging;

namespace ActualChat.Notification;

public class FirebaseMessagingClient(
    UrlMapper urlMapper,
    FirebaseMessaging firebaseMessaging,
    ICommander commander,
    ILogger<FirebaseMessagingClient> log)
{
    private UrlMapper UrlMapper { get; } = urlMapper;
    private FirebaseMessaging FirebaseMessaging { get; } = firebaseMessaging;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;
    private ILogger? DebugLog => !UrlMapper.IsActualChat ? Log : null;

    public async Task SendMessage(Notification entry, IReadOnlyCollection<Symbol> deviceIds, CancellationToken cancellationToken)
    {
        var (notificationId, _) = entry;
        var kind = entry.Kind;
        var title = entry.Title;
        var content = entry.Content;
        var iconUrl = entry.IconUrl;
        var chatId = entry.ChatId;
        var chatEntryNotification = entry.ChatEntryNotification;
        var chatEntryId = chatEntryNotification?.EntryId ?? default;
        var absoluteIconUrl = UrlMapper.ToAbsolute(iconUrl, true);
        var isDev = UrlMapper.IsDevActualChat;

        var isChatRelated = !chatId.IsNone;
        var isTextEntryRelated = chatEntryId is { IsNone: false, Kind: ChatEntryKind.Text };
        var tag = isTextEntryRelated
            ? chatEntryId.ChatId.Value
            : isChatRelated ? chatId.Value : "topic";
        var link = isTextEntryRelated
            ? UrlMapper.ToAbsolute(Links.Chat(chatId, chatEntryId.LocalId))
            : isChatRelated ? UrlMapper.ToAbsolute(Links.Chat(chatId)) : "";

        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            // We do not specify Notification instance, because we use Data messages to deliver notifications to Android
            // Notification = default,
            Data = new Dictionary<string, string>(StringComparer.Ordinal) {
                { Constants.Notification.MessageDataKeys.NotificationId, notificationId },
                { Constants.Notification.MessageDataKeys.Tag, tag },
                { Constants.Notification.MessageDataKeys.ChatId, chatId },
                { Constants.Notification.MessageDataKeys.ChatEntryId, chatEntryId },
                { Constants.Notification.MessageDataKeys.Icon, absoluteIconUrl },
                { Constants.Notification.MessageDataKeys.Link, link },
            },
            Android = new AndroidConfig {
                // We do not specify Notification instance, because we use Data messages to deliver notifications to Android
                // Notification = default,
                Data = new Dictionary<string, string>(StringComparer.Ordinal) {
                    { Constants.Notification.MessageDataKeys.Title, title },
                    { Constants.Notification.MessageDataKeys.Body, content },
                    { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
                },
                Priority = Priority.High,
                // CollapseKey = default, /* We don't use collapsible messages */
                TimeToLive = TimeSpan.FromMinutes(180),
            },
            Apns = new ApnsConfig {
                Aps = new Aps {
                    Alert = new ApsAlert {
                        Title = title,
                        Body = content,
                    },
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
                    Tag = tag,
                    RequireInteraction = false,
                    Icon = absoluteIconUrl,
                },
                FcmOptions = new WebpushFcmOptions {
                    Link = OrdinalEquals(UrlMapper.BaseUri.Host, "localhost")
                        ? null
                        : link,
                },
            },
        };
        if (isDev)
            multicastMessage.Android.FcmOptions = new AndroidFcmOptions {
                AnalyticsLabel = "dev_test" // Add label to see data messages statistics in Message delivery reports.
            };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        if (isDev) {
            var messageIds = string.Join(", ",
                batchResponse.Responses.Select(c =>
                    c.IsSuccess
                        ? c.MessageId
                        : c.Exception.MessagingErrorCode.HasValue
                            ? "errCode=" + c.Exception.MessagingErrorCode
                            : c.Exception.Message));
            DebugLog?.LogInformation("Sent {Successfully}/{Total} messages. Result: '{MessageIds}'",
                batchResponse.SuccessCount, batchResponse.Responses.Count, messageIds);
        }

        if (batchResponse.FailureCount > 0) {
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
                        .ToApiArray();
                    _ = Commander.Start(new NotificationsBackend_RemoveDevices(tokensToRemove), CancellationToken.None);
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
}
