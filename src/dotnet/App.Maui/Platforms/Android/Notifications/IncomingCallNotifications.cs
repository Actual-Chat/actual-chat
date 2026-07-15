using ActualChat.UI.Blazor.App.Services;
using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;
using Application = Android.App.Application;
using Person = AndroidX.Core.App.Person;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public static class IncomingCallNotifications
{
    public const string ChannelId = "incoming_calls_v2";
    // The v1 channel had its own ringtone; it's deleted so the ring plays from a single source
    // (the in-app looping ringer) instead of doubling with the channel sound.
    private const string LegacyChannelId = "incoming_calls";
    // Mirrors the server's LiveSessionsBackend.RingTimeout: the banner self-destructs
    // at ring expiry even when the dismissal push never arrives (offline device).
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(40);
    private static ILogger? _log;

    private static Context Context => Application.Context;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(IncomingCallNotifications));

    public static string DeclineAction => Context.PackageName + ".IncomingCall.Decline";
    public static string AcceptExtraKey => Context.PackageName + ".IncomingCall.Accept";
    public static string ChatIdExtraKey => Context.PackageName + ".IncomingCall.ChatId";
    public static string FullScreenExtraKey => Context.PackageName + ".IncomingCall.FullScreen";

    public static string CallTag(ChatId chatId)
        => Constants.Notification.CallTagPrefix + chatId.Value;

    // The single ring melody, shared by the notification channel (background/killed) and the
    // in-app looping ringer (foreground) so both play the same sound: the system default ringtone.
    public static Android.Net.Uri? RingtoneUri => RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);

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

        var fullScreenIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        fullScreenIntent.PutExtra(ChatIdExtraKey, chatId.Value);
        fullScreenIntent.PutExtra(FullScreenExtraKey, true);
        var fullScreenPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            fullScreenIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var callerBuilder = new Person.Builder()
            .SetName(data.Title ?? "Incoming call")!
            .SetImportant(true)!;
        var largeImage = data.ImageUrl.IsNullOrEmpty() ? null : NotificationHelper.GetImage(data.ImageUrl!);
        if (largeImage != null)
            callerBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
        var caller = callerBuilder.Build()!;

        // Native call notification: caller avatar/name, an "Incoming call" label, and prominent
        // Answer/Decline buttons the style builds from the intents (so no manual AddAction).
        var callStyle = NotificationCompat.CallStyle.ForIncomingCall(caller, declinePendingIntent, acceptPendingIntent)!;

        var builder = new NotificationCompat.Builder(Context, ChannelId)
            // ReSharper disable once AccessToStaticMemberViaDerivedType
            .SetSmallIcon(Microsoft.Maui.Resource.Drawable.notification_app_icon)!
            .SetColor(0x0036A3)!
            .SetContentIntent(contentPendingIntent)!
            .SetCategory(Android.App.Notification.CategoryCall)!
            .SetPriority((int)NotificationPriority.High)!
            .SetVisibility(NotificationCompat.VisibilityPublic)!
            .SetOngoing(true)!
            // Surfaces the ring over the lock screen / when the screen is off, and launches the
            // app's call UI there; on an unlocked screen it degrades to a heads-up banner.
            .SetFullScreenIntent(fullScreenPendingIntent, true)!
            .SetTimeoutAfter((long)RingTimeout.TotalMilliseconds)!
            .SetStyle(callStyle)!;
        NotificationManagerCompat.From(Context)!.Notify(tag, 0, builder.Build());
    }

    public static void Dismiss(ChatId chatId)
        => NotificationManagerCompat.From(Context)!.Cancel(CallTag(chatId), 0);

    public static void HandleViewIntent(Intent intent)
    {
        var chatId = ChatId.TryParse(intent.GetStringExtra(ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        if (intent.GetBooleanExtra(AcceptExtraKey, false)) {
            Dismiss(chatId);
            // Accept re-verifies the ring against LiveSessionUI.Get once Blazor is up —
            // a stale tap yields a "Call ended" toast, not a phantom join.
            _ = AppServicesAccessor.DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().Accept(chatId),
                "IncomingCallUI.Accept", whenRendered: true);
            return;
        }

        // Opened from the call notification (tap or full-screen intent): register the ring so the
        // in-app banner + looping ringer take over once the app is up.
        _ = AppServicesAccessor.DispatchToBlazor(
            c => c.GetRequiredService<IncomingCallUI>().OnRing(chatId),
            "IncomingCallUI.OnRing", whenRendered: true);
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
        notificationManager.DeleteNotificationChannel(LegacyChannelId);
        var channel = notificationManager.GetNotificationChannel(ChannelId);
        if (channel != null)
            return;

        // Silent, non-vibrating channel: the in-app ringer is the sole sound/vibration source, so
        // the channel must not ring on top of it. HIGH importance still drives the heads-up and
        // the full-screen intent that surfaces the call over the lock screen.
        channel = new NotificationChannel(ChannelId, "Incoming calls", NotificationImportance.High);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        notificationManager.CreateNotificationChannel(channel);
    }
}
