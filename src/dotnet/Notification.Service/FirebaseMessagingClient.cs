using FirebaseAdmin.Messaging;

namespace ActualChat.Notification;

public class FirebaseMessagingClient
{
    private readonly UriMapper _uriMapper;

    public FirebaseMessagingClient(UriMapper uriMapper)
        => _uriMapper = uriMapper;

    public async Task SendMessage(NotificationEntry entry, List<string> deviceIds, CancellationToken cancellationToken)
    {
        var (notificationId, _, notificationType, title, content, _) = entry;
        var tag = "topic";
        string link = null!;
        switch (notificationType) {
        case NotificationType.Message: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = entryId.HasValue
                    ? _uriMapper.ToAbsolute($"/chat/{chatId}#{entryId}").ToString()
                    : _uriMapper.ToAbsolute($"/chat/{chatId}").ToString();
            }
            break;
        }
        case NotificationType.Reply: {
            var chatId = entry.Message?.ChatId;
            var entryId = entry.Message?.EntryId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = entryId.HasValue
                    ? _uriMapper.ToAbsolute($"/chat/{chatId}#{entryId}").ToString()
                    : _uriMapper.ToAbsolute($"/chat/{chatId}").ToString();
            }
            break;
        }
        case NotificationType.Invitation: {
            var chatId = entry.Chat?.ChatId;
            if (!chatId.IsNullOrEmpty()) {
                tag = chatId;
                link = _uriMapper.ToAbsolute($"/chat/{chatId}").ToString();
            }
            break;
        }
        default:
            throw new ArgumentOutOfRangeException();
        }

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
    }
}
