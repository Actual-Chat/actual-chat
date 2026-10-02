using ActualChat.Localization;
using ActualChat.Maui;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using Android.App;
using Android.Content;
using Android.Media;
using AndroidX.Core.App;
using AndroidX.Core.Graphics.Drawable;
using Microsoft.Extensions.Localization;
using Application = Android.App.Application;
using Person = AndroidX.Core.App.Person;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NotificationExt = ActualChat.Notifications.NotificationExt;

namespace ActualChat.App.Maui;

public static class IncomingCallNotifications
{
    public const string ChannelId = "incoming_calls";
    // The banner self-destructs at ring expiry even when the dismissal push never arrives
    // (offline device). Shared with the server so the two can't drift - this was 40s against
    // the server's 20s.
    private static readonly TimeSpan RingTimeout = Constants.Call.RingTimeout;
    private static ILogger? _log;

    // The ring the user has already answered or declined - its call id, or its chat id when the push
    // named none. A full-screen intent dispatches with whenRendered: true, and on a cold start the
    // render is seconds away - so one queued before the answer runs after it and puts the ring
    // screen back over a settled call.
    private static string? _handledRing;

    private static Context Context => Application.Context;
    private static IStringLocalizer L => AppStrings.L;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(IncomingCallNotifications));
    private static ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

    public static string DeclineAction => Context.PackageName + ".IncomingCall.Decline";
    public static string AcceptExtraKey => Context.PackageName + ".IncomingCall.Accept";
    public static string ChatIdExtraKey => Context.PackageName + ".IncomingCall.ChatId";
    public static string CallIdExtraKey => Context.PackageName + ".IncomingCall.CallId";
    public static string FullScreenExtraKey => Context.PackageName + ".IncomingCall.FullScreen";

    public static string CallTag(ChatId chatId, CallId? callId)
        => callId is not null
            ? NotificationExt.GetCallTag(callId)
            : Constants.Notification.CallTagPrefix + chatId.Value;

    // The single ring melody, shared by the notification channel (background/killed) and the
    // in-app looping ringer (foreground) so both play the same sound: the system default ringtone.
    public static Android.Net.Uri? RingtoneUri => RingtoneManager.GetDefaultUri(RingtoneType.Ringtone);

    public static void Show(NotificationData data, TimeSpan timeout)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Show: no ChatId, messageId: '{MessageId}'", data.MessageId);
            return;
        }

        var callId = data.CallId;
        var tag = data.Tag ?? CallTag(chatId, callId);
        var link = data.Link ?? (string)Links.Chat(chatId);
        Show(chatId, callId, tag, link, data.Title, data.ImageUrl, timeout);
    }

    public static void Show(
        ChatId chatId,
        CallId? callId,
        string tag,
        string link,
        string? title,
        string? imageUrl,
        TimeSpan? timeout = null)
    {
        EnsureChannelExists();
        // A fresh ring is a call of its own: whatever the user did to the last one in this chat
        // must not silence its screen. A ring that names its call can't be mistaken for the last one.
        if (callId is null && Volatile.Read(ref _handledRing) == chatId.Value)
            Volatile.Write(ref _handledRing, null);

        var contentIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        PutCall(contentIntent);
        var contentPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            contentIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var acceptIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        PutCall(acceptIntent);
        acceptIntent.PutExtra(AcceptExtraKey, true);
        var acceptPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            acceptIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var declineIntent = new Intent(Context, typeof(CallActionReceiver));
        declineIntent.SetAction(DeclineAction);
        PutCall(declineIntent);
        var declinePendingIntent = PendingIntent.GetBroadcast(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            declineIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        // Surfaces the ring over the lock screen / when the screen is off by launching the Blazor
        // app there (FullScreenExtraKey makes MainActivity show over the keyguard); on an unlocked
        // screen it degrades to a heads-up banner.
        var fullScreenIntent = NotificationHelper.CreateViewIntent(Context, link)!;
        PutCall(fullScreenIntent);
        fullScreenIntent.PutExtra(FullScreenExtraKey, true);
        var fullScreenPendingIntent = PendingIntent.GetActivity(Context,
            NotificationHelper.RequestCodeProvider.IncrementAndGet(),
            fullScreenIntent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var callerBuilder = new Person.Builder()
            // Empty, not just null: CallStyle throws on a nameless Person, so a title-less push
            // would cost the ring its notification entirely.
            .SetName(title.NullIfEmpty() ?? L.Call_Incoming)!
            .SetImportant(true)!;
        var largeImage = imageUrl.IsNullOrEmpty() ? null : NotificationHelper.GetImage(imageUrl!);
        if (largeImage != null)
            callerBuilder.SetIcon(IconCompat.CreateWithBitmap(largeImage));
        var caller = callerBuilder.Build()!;

        // Native call notification: caller avatar/name, an "Incoming call" label, and prominent
        // Answer/Decline buttons the style builds from the intents (so no manual AddAction).
        var callStyle = NotificationCompat.CallStyle
            .ForIncomingCall(caller, declinePendingIntent, acceptPendingIntent)!;

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
            .SetTimeoutAfter((long)(timeout ?? RingTimeout).TotalMilliseconds)!
            .SetStyle(callStyle)!;
        NotificationHelper.MarkAsPushBanner(builder, tag);
        NotificationManagerCompat.From(Context)!.Notify(tag, 0, builder.Build());
        return;

        void PutCall(Intent intent) {
            intent.PutExtra(ChatIdExtraKey, chatId.Value);
            if (callId is not null)
                intent.PutExtra(CallIdExtraKey, callId.Value);
        }
    }

    public static void Dismiss(ChatId chatId, CallId? callId)
    {
        var notificationManager = NotificationManagerCompat.From(Context)!;
        if (callId is not null) {
            notificationManager.Cancel(CallTag(chatId, callId), 0);
            return;
        }

        // The ring was handled before anything named its call: whatever rings in this chat goes.
        foreach (var tag in ListActiveCallTags()) {
            if (NotificationExt.TryParseCallTag(tag)?.ChatId == chatId)
                notificationManager.Cancel(tag, 0);
        }
    }

    public static void MarkRingHandled(ChatId chatId, CallId? callId)
        // Call before dispatching the answer or the decline, not after: a full-screen intent
        // already waiting for the render has to find the ring spoken for by the time it runs.
        => Volatile.Write(ref _handledRing, callId?.Value ?? chatId.Value);

    public static CallId? GetCallId(Intent intent)
        => CallId.TryParse(intent.GetStringExtra(CallIdExtraKey), allowNull: true);

    public static void HandleViewIntent(Intent intent)
    {
        var chatId = ChatId.TryParse(intent.GetStringExtra(ChatIdExtraKey), allowNull: true);
        if (chatId is null)
            return;

        var callId = GetCallId(intent);
        if (intent.GetBooleanExtra(AcceptExtraKey, false)) {
            MarkRingHandled(chatId, callId);
            Dismiss(chatId, callId);
            // Blazor starting up sees the call already Active and would never stop a ring it didn't start.
            IncomingCallRinger.Stop();
            // Accept re-verifies the ring with the server once Blazor is up —
            // a stale tap yields a "Call ended" toast, not a phantom join.
            _ = AppServicesAccessor.DispatchToBlazor(
                c => c.GetRequiredService<CallScreensUI>().Accept(chatId, callId),
                "CallScreensUI.Accept", whenRendered: true);
            return;
        }

        // Opened from the call notification: register the ring so the in-app call UI + looping ringer
        // take over once the app is up. The full-screen-intent path (over the lock screen / screen
        // off) shows the full-screen call view instead of the modal; a plain tap shows the modal.
        var overLockScreen = intent.GetBooleanExtra(FullScreenExtraKey, false);
        if (overLockScreen && GetBlockedCallScreenGate() is var gate and not CallScreenGate.None) {
            // Android launches the activity either way; the gate only decides whether the keyguard
            // shows it. NotificationHandler already asked natively on a cold start, and that request
            // outlives the ring - it has to be taken back here, along with the cover it put up.
            DebugLog?.LogInformation(
                "CALL_TRACE: HandleViewIntent #{ChatId} - the over-lock screen is gated off ({Gate})",
                chatId, gate);
            MainActivity.Current?.DisableShowWhenLocked();
            overLockScreen = false;
        }
        DebugLog?.LogInformation(
            "CALL_TRACE: HandleViewIntent → dispatch OnRing #{ChatId}, overLockScreen={OverLockScreen}",
            chatId, overLockScreen);
        _ = AppServicesAccessor.DispatchToBlazor(
            c => {
                if (Volatile.Read(ref _handledRing) == (callId?.Value ?? chatId.Value)) {
                    DebugLog?.LogInformation(
                        "CALL_TRACE: dropping a stale OnRing #{ChatId} - the ring is already handled", chatId);
                    return;
                }

                c.GetRequiredService<CallScreensUI>().OnRing(chatId, overLockScreen);
            },
            "CallScreensUI.OnRing", whenRendered: true);
    }

    public static string[] ListActiveCallTags()
    {
        var notificationManager = NotificationManagerCompat.From(Context)!;
        var active = notificationManager.ActiveNotifications;
        if (active is null)
            return [];

        return active
            .Select(n => n.Tag)
            .Where(tag => NotificationExt.IsCallTag(tag))
            .Select(tag => tag!)
            .ToArray();
    }

    public static bool CanPostCalls()
    {
        // Fails open: only a definite "disabled" may keep a call from ringing.
        try {
            var notificationManager = NotificationManagerCompat.From(Context)!;
            if (!notificationManager.AreNotificationsEnabled())
                return false;

            var channel = notificationManager.GetNotificationChannel(ChannelId);
            return channel is not null && channel.Importance != NotificationImportance.None;
        }
        catch (Exception e) {
            Log.LogWarning(e, "CanPostCalls failed; assuming call notifications are shown");
            return true;
        }
    }

    // Private methods

    private static CallScreenGate GetBlockedCallScreenGate()
    {
        // Uncached on purpose: it's one read per incoming call, and a cached "blocked" would
        // outlive the moment the user grants the permission, for the rest of the process.
        try {
            return new AndroidFullScreenCallsAvailability(Log).ReadBlockedGate();
        }
        catch (Exception e) {
            // Fails open, like the gate checks themselves: a wrong "blocked" costs the call screen.
            Log.LogWarning(e, "Couldn't read the call-screen gate");
            return CallScreenGate.None;
        }
    }

    private static void EnsureChannelExists()
    {
        var notificationManager = (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
        // Silent, non-vibrating channel: the in-app ringer is the sole sound/vibration source, so
        // the channel must not ring on top of it. HIGH importance still drives the heads-up and
        // the full-screen intent that surfaces the call over the lock screen.
        var channel = new NotificationChannel(
            ChannelId, L.NotificationChannel_IncomingCalls, NotificationImportance.High);
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        notificationManager.CreateNotificationChannel(channel);
    }
}
