using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.App;
using Application = Android.App.Application;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public static class IncomingCallNotifications
{
    public const string ChannelId = "incoming_calls";
    // Mirrors the server's LiveSessionsBackend.RingTimeout: the banner self-destructs
    // at ring expiry even when the dismissal push never arrives (offline device).
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(40);
    private static ILogger? _log;

    private static Context Context => Application.Context;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(IncomingCallNotifications));

    public static string DeclineAction => Context.PackageName + ".IncomingCall.Decline";
    public static string AcceptExtraKey => Context.PackageName + ".IncomingCall.Accept";
    public static string ChatIdExtraKey => Context.PackageName + ".IncomingCall.ChatId";

    public static string CallTag(ChatId chatId)
        => Constants.Notification.CallTagPrefix + chatId.Value;

    public static void Show(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Show: no ChatId, messageId: '{MessageId}'", data.MessageId);
            return;
        }

        EnsureChannelExists();
        var tag = data.Tag ?? CallTag(chatId);
        var link = data.Link ?? (string)Links.Chat(chatId);

        var contentIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        contentIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        var contentPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var acceptIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        acceptIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        acceptIntent.PutExtra(AcceptExtraKey, true);
        var acceptPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            acceptIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var declineIntent = new Intent(Context, typeof(CallActionReceiver));
        declineIntent.SetAction(DeclineAction);
        declineIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        var declinePendingIntent = PendingIntent.GetBroadcast(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            declineIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(Context, ChannelId)
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentTitle(data.Title ?? "Incoming call")!
            .SetContentText(data.Body ?? "Incoming call")!
            .SetContentIntent(contentPendingIntent)!
            .SetCategory(Android.App.Notification.CategoryCall)!
            .SetPriority((int)NotificationPriority.High)!
            .SetAutoCancel(true)!
            .SetTimeoutAfter((long)RingTimeout.TotalMilliseconds)!;
        builder.AddAction(0, "Decline", declinePendingIntent);
        builder.AddAction(0, "Accept", acceptPendingIntent);
        var imageUrl = data.ImageUrl;
        if (!imageUrl.IsNullOrEmpty()) {
            var largeImage = NotificationHelper.GetImage(imageUrl!);
            if (largeImage != null)
                builder.SetLargeIcon(largeImage);
        }
        NotificationManagerCompat.From(Context)!.Notify(tag, 0, builder.Build());
    }

    public static void Dismiss(ChatId chatId)
        => NotificationManagerCompat.From(Context)!.Cancel(CallTag(chatId), 0);

    public static void HandleViewIntent(Intent intent)
    {
        if (!intent.GetBooleanExtra(AcceptExtraKey, false))
            return;

        var chatId = ChatId.TryParse(intent.GetStringExtra(ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        Dismiss(chatId);
        // Accept re-verifies the ring against LiveSessionUI.Get once Blazor is up —
        // a stale tap yields a "Call ended" toast, not a phantom join.
        _ = AppServicesAccessor.DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().Accept(chatId),
            "IncomingCallUI.Accept", whenRendered: true);
    }

    public static ChatId[] ListActiveCallChatIds()
    {
        var notificationManager = NotificationManagerCompat.From(Context)!;
        var active = notificationManager.ActiveNotifications;
        if (active is null)
            return [];

        return active
            .Select(n => n.Tag)
            .Where(tag => tag != null && tag.StartsWith(Constants.Notification.CallTagPrefix))
            .Select(tag => ChatId.TryParse(tag![Constants.Notification.CallTagPrefix.Length..], allowNull: true))
            .Where(chatId => chatId is not null)
            .Select(chatId => chatId!)
            .ToArray();
    }

    // Private methods

    private static void EnsureChannelExists()
    {
        var notificationManager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        var channel = notificationManager.GetNotificationChannel(ChannelId);
        if (channel != null)
            return;

        channel = new NotificationChannel(ChannelId, "Incoming calls", NotificationImportance.High);
        var attrs = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.NotificationRingtone)!
            .SetContentType(AudioContentType.Music)!
            .Build();
        var ringtoneUri = Android.Net.Uri.Parse($"android.resource://{Context.PackageName}/"
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            + Microsoft.Maui.Resource.Raw.attention_ringtone);
        channel.SetSound(ringtoneUri, attrs);
        channel.SetVibrationPattern([0, 700, 500, 700, 500, 500]);
        notificationManager.CreateNotificationChannel(channel);
    }
}
