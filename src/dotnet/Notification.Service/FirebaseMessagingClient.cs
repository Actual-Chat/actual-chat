using ActualChat.Notification.Backend;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notification;

public class FirebaseMessagingClient
{
    private readonly UriMapper _uriMapper;
    private readonly FirebaseMessaging _firebaseMessaging;
    private readonly ICommander _commander;
    private readonly ILogger<FirebaseMessagingClient> _log;

    public FirebaseMessagingClient(
        UriMapper uriMapper,
        FirebaseMessaging firebaseMessaging,
        ICommander commander,
        ILogger<FirebaseMessagingClient> log)
    {
        _uriMapper = uriMapper;
        _firebaseMessaging = firebaseMessaging;
        _commander = commander;
        _log = log;
    }

    public async Task SendMessage(NotificationEntry entry, List<string> deviceIds, CancellationToken cancellationToken)
    {
        var (notificationId, notificationType, title, content, _) = entry;
        var tag = "topic";
        string link = null!;
        switch (notificationType) {
        case NotificationType.Message: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = _uriMapper.GetChatUrl(chatId, entryId).ToString();
            }
            break;
        }
        case NotificationType.Reply: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = _uriMapper.GetChatUrl(chatId, entryId).ToString();
            }
            break;
        }
        case NotificationType.Invitation: {
            var chatId = entry.Chat?.ChatId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = _uriMapper.GetChatUrl(chatId).ToString();
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
            },
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
                    Tag = tag,
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
                    Tag = tag,
                    RequireInteraction = false,
                    // Icon = ??? TODO(AK): Set icon
                },
                FcmOptions = new WebpushFcmOptions {
                    Link = OrdinalEquals(_uriMapper.BaseUri.Host, "localhost")
                        ? null
                        : link,
                }
            },
        };
        var batchResponse = await _firebaseMessaging.SendMulticastAsync(multicastMessage, cancellationToken)
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
    }
}
