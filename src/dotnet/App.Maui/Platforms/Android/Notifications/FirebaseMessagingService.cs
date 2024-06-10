using ActualChat.UI.Blazor.Services;
using Android.App;
using AndroidX.Core.App;
using Firebase.Analytics;
using Firebase.Messaging;

namespace ActualChat.App.Maui;

[Service(Exported = true)]
#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
#pragma warning restore CA1861
public class FirebaseMessagingService : Firebase.Messaging.FirebaseMessagingService
{
    private static ILogger? _log;
    /**
    * Request code used by display notification pending intents.
    *
    * Android only keeps one PendingIntent instance if it thinks multiple pending intents match.
    * Our intents often only differ by the payload which is stored in intent extras. As comparing
    * PendingIntents/Intents does not inspect the payload data, multiple pending intents, such as the
    * ones for click/dismiss will conflict.
    *
    * We also need to avoid conflicts with notifications started by an earlier launch of the app,
    * so use the truncated uptime of when the class was instantiated. The uptime will only overflow
    * every ~50 days, and even then chances of conflict will be rare.
    */

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<FirebaseMessagingService>();

#if IS_DEV_MAUI
 #pragma warning disable CS0169 // Field is never used
 #pragma warning disable CA1823
    // Keep reference to FirebaseAnalytics type to ensure FA package is used and will be initialized.
    private FirebaseAnalytics? _firebaseAnalytics;
 #pragma warning restore CA1823
 #pragma warning restore CS0169 // Field is never used
#endif

    public override void OnNewToken(string token)
    {
        Log.LogDebug("OnNewToken: '{Token}'", token);
        base.OnNewToken(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        Log.LogDebug("OnMessageReceived: message #{MessageId}, CollapseKey='{CollapseKey}'" +
            ", Priority={Priority}, OriginalPriority={OriginalPriority}, IsDeprioritized={IsDeprioritized}",
            message.MessageId, message.CollapseKey, message.Priority, message.OriginalPriority, message.Priority != message.OriginalPriority);

        // There are 2 types of messages:
        // https://firebase.google.com/docs/cloud-messaging/concept-options#notifications_and_data_messages
        // Now we use Data message to deliver notifications to Android.
        // This allows us to control notification display style both when app is in foreground and in background modes.
        var dataRaw = message.Data.ToDictionary(StringComparer.Ordinal);
        if (Log.IsEnabled(LogLevel.Debug)) {
            var dataAsText = dataRaw.Select(c => $"'{c.Key}':'{c.Value}'").ToCommaPhrase();
            Log.LogDebug("OnMessageReceived: message #{MessageId}, Data: {Data}",
                message.MessageId, dataAsText);
        }

        var data = new NotificationData(message.MessageId, dataRaw);

        if (data.NotificationKind == NotificationKind.GetAttention
            && ShowGetAttentionNotification(data, message.SentTime))
            return;

        if (data.Title.IsNullOrEmpty() || data.Body.IsNullOrEmpty())
            return;

        if (AndroidUtils.IsAppForeground() ?? false) {
            var chatId = data.ChatId;
            if (!chatId.IsNone && TryGetScopedServices(out var scopedServices)) {
                var history = scopedServices.GetRequiredService<History>();
                if (history.LocalUrl.IsChat(out var currentChatId) && currentChatId == chatId) {
                    Log.LogDebug("OnMessageReceived: notification in the current chat #{ChatId}", chatId);
                    return;
                }
            }
        }

        ShowChatMessageNotification(data);
    }

    // Private methods
    private static bool ShowGetAttentionNotification(
        NotificationData data,
        long messageSentTime)
    {
        var chatId = data.ChatId;
        if (chatId.IsNone) {
            Log.LogWarning("Can't show get-attention notification. Invalid ChatId. Ref messageId: '{MessageId}'", data.MessageId);
            return false;
        }
        var sentTime = new Moment(messageSentTime * 10_000).ToDateTime();
        var title = data.Title ?? "";
        var separatorIndex = title.IndexOf('@');
        if (separatorIndex >= 0) {
            // var part1 = title.Substring(0, separatorIndex).Trim();
            var part2 = title.Substring(separatorIndex + 1).Trim();
            title = part2;
        }

        var request = new ChatAttentionRequest(chatId, data.LastEntryLocalId, sentTime, title, data.Body ?? "", data.ImageUrl ?? "");
        ChatAttentionService.Instance.Ask(request);
        return true;
    }

    private void ShowChatMessageNotification(NotificationData data)
    {
        // var intent = new Intent(this, typeof(MainActivity));
        // intent.AddFlags(ActivityFlags.SingleTop);
        var contentIntent = NotificationHelper.CreateViewIntent(this, data.Link);

        var body = data.Body!;
        Log.LogDebug("-> ShowChatMessageNotification, text: '{Text}'", body);

        // Generate an unique(ish) request code for a PendingIntent.
        //var pendingIntentRequestCode = NotificationHelper.RequestCodeProvider.IncrementAndGet();
        var contentPendingIntent = PendingIntent.GetActivity(this, 0,
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var notificationBuilder = new NotificationCompat.Builder(this, NotificationHelper.Constants.DefaultChannelId)
            .SetContentTitle(data.Title!)
            // The small icon should be opaque white
            // https://doc.batch.com/android/advanced/customizing-notifications/#setting-up-custom-push-icons
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            .SetSmallIcon(Resource.Drawable.notification_app_icon)
            .SetColor(0x0036A3)
            .SetContentText(body)
            .SetContentIntent(contentPendingIntent)
            .SetAutoCancel(true) // closes notification after tap
            .SetPriority((int)NotificationPriority.High);
        var imageUrl = data.ImageUrl;
        if (imageUrl != null) {
            var largeImage = NotificationHelper.GetImage(imageUrl);
            if (largeImage != null)
                notificationBuilder.SetLargeIcon(largeImage);
        }
        var notification = notificationBuilder.Build();
        var notificationManager = NotificationManagerCompat.From(this);
        notificationManager.Notify(data.Tag, 0, notification);
    }
}
