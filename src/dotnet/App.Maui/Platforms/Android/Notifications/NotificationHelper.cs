using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
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

    public static Bitmap? GetImage(string imageUrl)
        => imageUrl.IsNullOrEmpty()
            ? null
            : ImagesCache.GetOrCreate(imageUrl, DownloadImage);

    private static Bitmap? DownloadImage(string imageUrl)
    {
        var imageDownload = AndroidUtils.StartImageDownloadInBackground(imageUrl.ToUri());
        var largeImage = AndroidUtils.WaitForAndApplyImageDownload(imageDownload);
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
