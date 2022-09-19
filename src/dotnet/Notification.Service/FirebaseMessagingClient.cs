using ActualChat.Notification.Backend;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notification;

public class FirebaseMessagingClient
{
    private UriMapper UriMapper { get; }
    private FirebaseMessaging FirebaseMessaging { get; }
    private ICommander Commander { get; }
    private ILogger Log { get; }

    public FirebaseMessagingClient(
        UriMapper uriMapper,
        FirebaseMessaging firebaseMessaging,
        ICommander commander,
        ILogger<FirebaseMessagingClient> log)
    {
        UriMapper = uriMapper;
        FirebaseMessaging = firebaseMessaging;
        Commander = commander;
        Log = log;
    }

    public async Task SendMessage(NotificationEntry entry, List<string> deviceIds, CancellationToken cancellationToken)
    {
        var (notificationId, notificationType, title, content, iconUrl, _) = entry;
        var absoluteIconUrl = UriMapper.ToAbsolute(iconUrl).ToString();
        var tag = "topic";
        string link = null!;
        switch (notificationType) {
        case NotificationType.Message: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = UriMapper.ToAbsolute(Links.ChatPage(chatId, entryId)).ToString();
            }
            break;
        }
        case NotificationType.Reply: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = UriMapper.ToAbsolute(Links.ChatPage(chatId, entryId)).ToString();
            }
            break;
        }
        case NotificationType.Invitation: {
            var chatId = entry.Chat?.ChatId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = UriMapper.ToAbsolute(Links.ChatPage(chatId)).ToString();
            }
            break;
        }
        default:
            throw new InvalidOperationException("NotificationType is not supported.");
        }

        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds,
            Data = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase) {
                { "notificationId", notificationId },
                { "tag", tag },
                { "icon", absoluteIconUrl },
            },
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = title,
                Body = content,
                ImageUrl = absoluteIconUrl,
            },
            Android = new AndroidConfig {
                Notification = new AndroidNotification {
                    // Color = ??? TODO(AK): set color
                    Priority = NotificationPriority.DEFAULT,
                    // Sound = ??? TODO(AK): set sound
                    Tag = tag,
                    Visibility = NotificationVisibility.PRIVATE,
                    // ClickAction = ?? TODO(AK): Set click action for Android
                    DefaultSound = true,
                    LocalOnly = false,
                    // NotificationCount = TODO(AK): Set unread message count!
                    Icon = absoluteIconUrl,
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
                    Link = OrdinalEquals(UriMapper.BaseUri.Host, "localhost")
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
                if (responseGroup.Key == MessagingErrorCode.Unregistered) {
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
