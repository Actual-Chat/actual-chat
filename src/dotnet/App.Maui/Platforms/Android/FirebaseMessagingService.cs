using ActualChat.Notification;
using ActualChat.Notification.UI.Blazor;
using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using Firebase.Messaging;
using AtomicInteger = Java.Util.Concurrent.Atomic.AtomicInteger;

namespace ActualChat.App.Maui;

[Service(Exported = true)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class FirebaseMessagingService : Firebase.Messaging.FirebaseMessagingService
{
    private const int ImageCacheSize = 5;

    private static readonly ThreadSafeLruCache<string, Bitmap?> _imagesCache = new (ImageCacheSize);
    private static FirebaseMessagingUtils? _utils;
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
    private static readonly AtomicInteger _requestCodeProvider =
        new AtomicInteger((int) Android.OS.SystemClock.ElapsedRealtime());

    private ILogger Log { get; set; } = NullLogger.Instance;

    public override void OnCreate()
    {
        _utils ??= new FirebaseMessagingUtils(ApplicationContext!);
        Log = AppServices.LogFor<FirebaseMessagingService>();
    }

    public override void OnNewToken(string token)
    {
        Log.LogDebug("OnNewToken: '{Token}'", token);
        base.OnNewToken(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        Log.LogDebug("OnMessageReceived: message #{MessageId}, CollapseKey='{CollapseKey}'",
            message.MessageId, message.CollapseKey);

        base.OnMessageReceived(message);

        string? title;
        string? text;
        string? imageUrl;

        // There are 2 types of messages:
        // https://firebase.google.com/docs/cloud-messaging/concept-options#notifications_and_data_messages
        var notification = message.GetNotification();
        var data = message.Data;
        // Now we use Data message to deliver notifications to Android.
        // This allows us to control notification display style both when app is in foreground and in background modes.
        if (notification == null) {
            data.TryGetValue(NotificationConstants.MessageDataKeys.Title, out title);
            data.TryGetValue(NotificationConstants.MessageDataKeys.Body, out text);
            data.TryGetValue(NotificationConstants.MessageDataKeys.ImageUrl, out imageUrl);
        }
        else {
            // Backward compatibility, we still can accept notification messages.
            title = notification.Title;
            text = notification.Body;
            imageUrl = notification.ImageUrl.ToString();
        }
        if (title.IsNullOrEmpty() || text.IsNullOrEmpty())
            return;

        if (_utils!.IsAppForeground()) {
            data.TryGetValue(NotificationConstants.MessageDataKeys.ChatId, out var sChatId);
            var chatId = new ChatId(sChatId, ParseOrNone.Option);
            if (!chatId.IsNone && TryGetScopedServices(out var scopedServices)) {
                var handler = scopedServices.GetRequiredService<NotificationUI>();
                if (handler.IsAlreadyThere(chatId)) {
                    // Do nothing if notification leads to the active chat.
                    Log.LogDebug(
                        "OnMessageReceived: notification in the active chat while app is foreground, chat #{ChatId}",
                        chatId);
                    return;
                }
            }
        }

        ShowNotification(title, text, imageUrl, message.Data);
    }

    private void ShowNotification(
        string title,
        string text,
        string? imageUrl,
        IDictionary<string, string> data)
    {
        data.TryGetValue("tag", out var tag);
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop);

        foreach (var key in data.Keys) {
            string value = data[key];
            intent.PutExtra(key, value);
        }

        if (Log.IsEnabled(LogLevel.Debug)) {
            var dataAsText = data.Select(c => $"'{c.Key}':'{c.Value}'").ToCommaPhrase();
            Log.LogDebug("About to show ShowNotification '{Text}'. Data: {Data}", text, dataAsText);
        }

        // Generate an unique(ish) request code for a PendingIntent.
        var pendingIntentRequestCode = _requestCodeProvider.IncrementAndGet();
        var pendingIntent = PendingIntent.GetActivity(this, pendingIntentRequestCode,
            intent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var notificationBuilder = new NotificationCompat.Builder(this, NotificationConstants.ChannelIds.Default)
            .SetContentTitle(title)
            // The small icon should be opaque white
            // https://doc.batch.com/android/advanced/customizing-notifications/#setting-up-custom-push-icons
            .SetSmallIcon(Resource.Drawable.notification_app_icon)
            .SetColor(0x0036A3)
            .SetContentText(text)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true) // closes notification after tap
            .SetPriority((int)NotificationPriority.High);
        if (imageUrl != null) {
            var largeImage = ResolveImage(imageUrl);
            if (largeImage != null)
                notificationBuilder.SetLargeIcon(largeImage);
        }
        var notification = notificationBuilder.Build();
        var notificationManager = NotificationManagerCompat.From(this);
        notificationManager.Notify(tag, 0, notification);
    }

    private Bitmap? ResolveImage(string imageUrl)
        => imageUrl.IsNullOrEmpty()
            ? null
            : _imagesCache.GetOrCreate(imageUrl, DownloadImage);

    private static Bitmap? DownloadImage(string imageUrl)
    {
        var imageDownloader = FirebaseMessagingUtils.StartImageDownloadInBackground(new Uri(imageUrl));
        var largeImage = FirebaseMessagingUtils.WaitForAndApplyImageDownload(imageDownloader);
        return largeImage;
    }
}
