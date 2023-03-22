using ActualChat.Notification.Backend;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notification;

public class FirebaseMessagingClient
{
    private UrlMapper UrlMapper { get; }
    private FirebaseMessaging FirebaseMessaging { get; }
    private ICommander Commander { get; }
    private ILogger Log { get; }

    public FirebaseMessagingClient(
        UrlMapper urlMapper,
        FirebaseMessaging firebaseMessaging,
        ICommander commander,
        ILogger<FirebaseMessagingClient> log)
    {
        UrlMapper = urlMapper;
        FirebaseMessaging = firebaseMessaging;
        Commander = commander;
        Log = log;
    }

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
            Data = new Dictionary<string, string>(StringComparer.Ordinal) {
                { NotificationConstants.MessageDataKeys.NotificationId, notificationId },
                { NotificationConstants.MessageDataKeys.Tag, tag },
                { NotificationConstants.MessageDataKeys.ChatId, chatId },
                { NotificationConstants.MessageDataKeys.ChatEntryId, chatEntryId },
                { NotificationConstants.MessageDataKeys.Icon, absoluteIconUrl },
                { NotificationConstants.MessageDataKeys.Link, link },
            },
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = title,
                Body = content,
                // ImageUrl = TODO(AK): add first image url if the message contains images
            },
            Android = new AndroidConfig {
                Notification = new AndroidNotification {
                    // Color = ??? TODO(AK): set color
                    // For test purpose put priority to high
                    // To have notification message appears on top of screen no matter what type notification it is.
                    // Later I want to keep this behavior only for 'mention' and 'reply' messages.
                    // Normal messages will be shown only in system tray without popping up on top of a screen.
                    Priority = NotificationPriority.HIGH,
                    // Sound = ??? TODO(AK): set sound
                    Tag = tag,
                    Visibility = NotificationVisibility.PRIVATE,
                    // ClickAction = ?? TODO(AK): Set click action for Android
                    DefaultSound = true,
                    LocalOnly = false,
                    // NotificationCount = TODO(AK): Set unread message count!
                    //Icon = default, /* contains icon resource for android app, specify nothing, use app icon */
                    //ImageUrl = default, /* default notification layout expands image and it looks ugly, so we do not specify an image */
                    ChannelId = NotificationConstants.ChannelIds.Default,
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
        var batchResponse = await FirebaseMessaging.SendMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);

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
                        .ToImmutableArray();
                    _ = Commander.Start(new INotificationsBackend.RemoveDevicesCommand(tokensToRemove), CancellationToken.None);
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
