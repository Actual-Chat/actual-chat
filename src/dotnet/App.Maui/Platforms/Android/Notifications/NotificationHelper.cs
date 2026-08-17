using ActualChat.Notifications;
using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;
using Application = Android.App.Application;
using Person = AndroidX.Core.App.Person;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using AtomicInteger = Java.Util.Concurrent.Atomic.AtomicInteger;

namespace ActualChat.App.Maui;

public static class NotificationHelper
{
    private const int ImageCacheSize = 5;
    private const int MaxUploadLines = 5;
    private static readonly ThreadSafeLruCache<string, Bitmap?> ImagesCache = new (ImageCacheSize);
    private static ILogger? _log;
    public static readonly AtomicInteger RequestCodeProvider =
        new((int)Android.OS.SystemClock.ElapsedRealtime());
    public static string NotificationViewAction => Application.Context.PackageName + ".NotificationView";
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(NotificationHelper));

    public static void EnsureActivityChannelsExist(Context context)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(Constants.ActivityUploadChannelId) != null)
            return;

        var channel = new NotificationChannel(
            Constants.ActivityUploadChannelId,
            "Uploads",
            NotificationImportance.Low);
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    // Builds and shows a chat notification under the given tag. Shared by the FCM receive path
    // and the client reconciler's create-missing path so they stay identical.
    public static void ShowChatNotification(
        string tag, string title, string body, string? imageUrl, string? link,
        bool silent = false, IReadOnlyList<PushMessage>? messages = null)
    {
        var context = Application.Context;
        var contentIntent = CreateViewIntent(context, link);
        var contentPendingIntent = PendingIntent.GetActivity(context, 0,
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var largeImage = imageUrl.IsNullOrEmpty() ? null : GetImage(imageUrl!);
        var (senderName, conversationTitle) = SplitTitle(title);
        var style = CreateStyle(senderName, conversationTitle, body, largeImage, messages);
        var builder = new NotificationCompat.Builder(context, Constants.DefaultChannelId)
            .SetContentTitle(title)!
            .SetSmallIcon(Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentText(body)!
            .SetContentIntent(contentPendingIntent)!
            .SetAutoCancel(true)!
            // A silent update re-posts under the same tag without alerting again.
            .SetSilent(silent)!
            .SetPriority((int)NotificationPriority.High)!;
        // MessagingStyle hides both the content title and (on many launchers) its conversation title.
        if (style is NotificationCompat.MessagingStyle && !conversationTitle.IsNullOrEmpty())
            builder.SetSubText(conversationTitle);
        builder.SetStyle(style);
        if (largeImage != null)
            builder.SetLargeIcon(largeImage);
        NotificationManagerCompat.From(context)!.Notify(tag, 0, builder.Build());
    }

    // Telegram-style rendering: one MessagingStyle line per pushed message (sender + text +
    // timestamp) when structured messages are present; single-message and BigTextStyle fallbacks
    // keep old-server pushes and non-chat titles working.
    private static NotificationCompat.Style CreateStyle(
        string senderName,
        string? conversationTitle,
        string body,
        Bitmap? largeImage,
        IReadOnlyList<PushMessage>? messages)
    {
        var bigText = new NotificationCompat.BigTextStyle().BigText(body)!;
        if (senderName.IsNullOrEmpty())
            return bigText;

        try {
            var self = new Person.Builder().SetName("You")!.Build();
            var style = new NotificationCompat.MessagingStyle(self);
            if (!conversationTitle.IsNullOrEmpty()) {
                style.SetGroupConversation(true);
                style.SetConversationTitle(conversationTitle);
            }
            if (messages is { Count: > 0 }) {
                // Only the newest sender carries the avatar — it's the banner headline's icon.
                var newestName = messages[^1].AuthorName.NullIfEmpty() ?? senderName;
                var persons = new Dictionary<string, Person>();
                foreach (var message in messages) {
                    var name = message.AuthorName.NullIfEmpty() ?? senderName;
                    if (!persons.TryGetValue(name, out var person)) {
                        var personBuilder = new Person.Builder().SetName(name)!;
                        if (name == newestName && largeImage != null)
                            personBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
                        person = personBuilder.Build();
                        persons.Add(name, person);
                    }
                    style.AddMessage(message.Text, message.SentAtMs, person);
                }
            }
            else {
                var senderBuilder = new Person.Builder().SetName(senderName)!;
                if (largeImage != null)
                    senderBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
                style.AddMessage(body, Java.Lang.JavaSystem.CurrentTimeMillis(), senderBuilder.Build());
            }
            return style;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to build MessagingStyle; falling back to BigTextStyle");
            return bigText;
        }
    }

    private static (string SenderName, string? ConversationTitle) SplitTitle(string title)
    {
        var index = title.IndexOf(" @ ");
        return index >= 0
            ? (title[..index].Trim(), title[(index + 3)..].Trim())
            : (title, null);
    }

    public static Bitmap? GetImage(string imageUrl)
        => imageUrl.IsNullOrEmpty()
            ? null
            : ImagesCache.GetOrCreate(imageUrl, DownloadImage);

    public static Task<Bitmap?> GetImageAsync(string imageUrl)
    {
        if (imageUrl.IsNullOrEmpty())
            return Task.FromResult<Bitmap?>(null);

        if (ImagesCache.TryGetValue(imageUrl, out var bitmap))
            return Task.FromResult<Bitmap?>(bitmap);

        var tcs = TaskCompletionSourceExt.New<Bitmap?>();
        _ = BackgroundTask.Run(() => {
            var bitmap2 = ImagesCache.GetOrCreate(imageUrl, DownloadImage);
            tcs.TrySetResult(bitmap2);
            return Task.CompletedTask;
        });
        return tcs.Task;
    }

    private static Bitmap? DownloadImage(string imageUrl)
    {
        var sw = Stopwatch.GetTimestamp();
        Bitmap? largeImage = null;
        try {
            var imageDownload = AndroidUtils.StartImageDownloadInBackground(imageUrl.ToUri());
            largeImage = AndroidUtils.WaitForAndApplyImageDownload(imageDownload);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to download image '{Url}'", imageUrl);
        }
        var elapsed = (int)Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
        Log.Log(elapsed > 5000 ? LogLevel.Warning : LogLevel.Information,
            "Downloading image '{Url}' took {Elapsed} ms",
            imageUrl, elapsed);
        return largeImage;
    }

    public static Intent? CreateViewIntent(Context context, string? link)
    {
        var uri = !link.IsNullOrEmpty() ? Android.Net.Uri.Parse(link) : null;
        if (uri != null)
            return new Intent(NotificationViewAction, uri, context, typeof(MainActivity));

        // Query the package manager for the best launch intent for the app
        var intent = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!);
        if (intent == null)
            Log.LogWarning("No activity found to launch app");
        return intent;
    }

    public static void EnsureDefaultNotificationChannelExist(Context context, string channelId)
    {
        var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        // After you create a notification channel,
        // you cannot change the notification behaviors—the user has complete control at that point.
        // Though you can still change a channel's name and description.
        // https://developer.android.com/develop/ui/views/notifications/channels
        var channel = new NotificationChannel(channelId, "Default", NotificationImportance.High);
        notificationManager.CreateNotificationChannel(channel);
    }

    public static void EnsureAttentionNotificationChannelExist(Context context, string channelId)
    {
        var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        var channel = notificationManager.GetNotificationChannel(channelId);
        if (channel == null) {
            channel = new NotificationChannel(channelId,
                "Attention required",
                NotificationImportance.High);
            var attrs = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.NotificationRingtone)!
                .SetContentType(AudioContentType.Music)!
                .Build();
            //var ringtoneUri = RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);
            var ringtoneUri = Android.Net.Uri.Parse($"android.resource://{context.PackageName}/"
                // ReSharper disable once AccessToStaticMemberViaDerivedType
                + Microsoft.Maui.Resource.Raw.attention_ringtone);
            channel.SetSound(ringtoneUri, attrs);
            var vibratePattern = new long[]{0, 700, 500, 700, 500, 500};
            channel.SetVibrationPattern(vibratePattern);
            notificationManager.CreateNotificationChannel(channel);
        }
    }

    // Shared by the foreground service (when uploading is the primary activity) and
    // AndroidActivitiesBackend (when it isn't and needs its own notification).
    public static Android.App.Notification BuildUploadNotification(
        Context context,
        UploadActivity upload)
    {
        var percent = upload.TotalBytes == 0 ? 0 : (int)(100.0 * upload.BytesUploaded / upload.TotalBytes);
        var title = upload.FileCount == 1 ? "Uploading 1 file" : $"Uploading {upload.FileCount} files";
        var summary = $"{FormatBytes(upload.BytesUploaded)} / {FormatBytes(upload.TotalBytes)} ({percent}%)";

        var style = new NotificationCompat.InboxStyle().SetSummaryText(summary)!;
        var count = Math.Min(upload.Items.Count, MaxUploadLines);
        for (var i = 0; i < count; i++) {
            var item = upload.Items[i];
            _ = style.AddLine($"{item.FileName} — {FormatBytes(item.BytesUploaded)} / {FormatBytes(item.TotalBytes)}");
        }
        if (upload.Items.Count > MaxUploadLines)
            _ = style.AddLine($"…and {upload.Items.Count - MaxUploadLines} more");

        var viewIntent = CreateViewIntent(context, Links.Home);
        var viewPending = viewIntent is null
            ? null
            : PendingIntent.GetActivity(context, 3, viewIntent, PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, Constants.ActivityUploadChannelId)
            .SetSmallIcon(Resource.Drawable.notification_app_icon)!
            .SetContentTitle(title)!
            .SetContentText(summary)!
            .SetStyle(style)!
            .SetProgress(100, percent, indeterminate: false)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetCategory(NotificationCompat.CategoryProgress)!;
        if (viewPending is not null)
            _ = builder.SetContentIntent(viewPending);
        return builder.Build()!;
    }

    // Private methods

    private static string FormatBytes(long bytes)
    {
        const double kb = 1024.0;
        const double mb = 1024.0 * 1024.0;
        const double gb = 1024.0 * 1024.0 * 1024.0;
        if (bytes < kb)
            return $"{bytes} B";
        if (bytes < mb)
            return $"{bytes / kb:0.#} KB";
        if (bytes < gb)
            return $"{bytes / mb:0.#} MB";

        return $"{bytes / gb:0.##} GB";
    }

    // Nested types

    public static class Constants
    {
        public const string DefaultChannelId = "default_channel";
        public const string AttentionChannelId = "internal_attention_channel";
        public const string ActivityUploadChannelId = "activity_upload";
        public const int UploadNotificationId = 3002;
    }
}
