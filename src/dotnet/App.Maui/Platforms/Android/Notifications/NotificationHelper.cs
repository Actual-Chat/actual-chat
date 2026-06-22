using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using AndroidX.Core.App;
using Application = Android.App.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using AtomicInteger = Java.Util.Concurrent.Atomic.AtomicInteger;

namespace ActualChat.App.Maui;

public static class NotificationHelper
{
    private const int ImageCacheSize = 5;

    private static readonly ThreadSafeLruCache<string, Bitmap?> ImagesCache = new (ImageCacheSize);
    private static ILogger? _log;

    public static readonly AtomicInteger RequestCodeProvider =
        new((int)Android.OS.SystemClock.ElapsedRealtime());

    public static string NotificationViewAction => Application.Context.PackageName + ".NotificationView";

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(NotificationHelper));

    public static class Constants
    {
        public const string DefaultChannelId = "default_channel";
        public const string AttentionChannelId = "internal_attention_channel";
    }

    // Builds and shows a chat notification under the given tag. Shared by the FCM receive path
    // and the client reconciler's create-missing path so they stay identical.
    public static void ShowChatNotification(string tag, string title, string body, string? imageUrl, string? link)
    {
        var context = Application.Context;
        var contentIntent = CreateViewIntent(context, link);
        var contentPendingIntent = PendingIntent.GetActivity(context, 0,
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(context, Constants.DefaultChannelId)
            .SetContentTitle(title)!
            .SetSmallIcon(Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentText(body)!
            .SetContentIntent(contentPendingIntent)!
            .SetAutoCancel(true)!
            .SetPriority((int)NotificationPriority.High)!;
        if (!imageUrl.IsNullOrEmpty()) {
            var largeImage = GetImage(imageUrl);
            if (largeImage != null)
                builder.SetLargeIcon(largeImage);
        }
        NotificationManagerCompat.From(context)!.Notify(tag, 0, builder.Build());
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
}
