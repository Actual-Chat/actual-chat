using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace ActualChat.Notification;

public class NotificationPublisher : INotificationPublisher
{
    private readonly FirebaseMessaging _firebaseMessaging;

    public NotificationPublisher()
    {
        var firebaseApp = FirebaseApp.Create(new AppOptions {
            Credential = GoogleCredential.GetApplicationDefault(),
        });
        _firebaseMessaging = FirebaseMessaging.GetMessaging(firebaseApp);
    }

    public Task Publish(NotificationEntry notification, CancellationToken cancellationToken)
    {
        switch (notification) {
            case UserNotificationEntry userEntry:
                return PublishUserNotification(userEntry, cancellationToken);
            case TopicNotificationEntry topicEntry:
                return PublishTopicNotification(topicEntry, cancellationToken);
            case null:
                throw new ArgumentException("notification should not be null.", nameof(notification));
            default:
                throw new NotSupportedException(notification.GetType().Name + " is not supported.");
        }
    }

    private async Task PublishTopicNotification(
        TopicNotificationEntry topicNotificationEntry,
        CancellationToken cancellationToken)
    {
        var topicMessage = new Message {
            Topic = topicNotificationEntry.TopicId,
            Notification = new FirebaseAdmin.Messaging.Notification {
                Title = topicNotificationEntry.Title,
                Body = topicNotificationEntry.Content,
                // ImageUrl = ??? TODO(AK): set image url
            },
            Android = new AndroidConfig {
                Notification = new AndroidNotification {
                    // Color = ??? TODO(AK): set color
                    Priority = NotificationPriority.DEFAULT,
                    // Sound = ??? TODO(AK): set sound
                    Tag = topicNotificationEntry.TopicId,
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
                    Renotify = true,
                    Tag = topicNotificationEntry.TopicId,
                    RequireInteraction = false,
                    // Icon = ??? TODO(AK): Set icon
                },
                FcmOptions = new WebpushFcmOptions {
                    // Link = ??? TODO(AK): Set topic Url to be opened with notification click
                }
            },
        };
        await _firebaseMessaging.SendAsync(topicMessage, cancellationToken).ConfigureAwait(false);
    }

    private Task PublishUserNotification(
        UserNotificationEntry userNotificationEntry,
        CancellationToken cancellationToken)
    {
        var userId = userNotificationEntry.UserId;

        throw new NotImplementedException();
    }
}
