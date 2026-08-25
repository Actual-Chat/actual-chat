using ActualChat.App.Maui.Activities;
using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;
using IntentExtras = ActualChat.App.Maui.Activities.AndroidActivitiesForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Android shell for PTT wakes: FGS lifecycle + FCM entry point;
/// the portable core lives in <see cref="PttSession"/>.
/// </summary>
public static class PttWakeHandler
{
    private const string InitialTitle = "Push-to-talk";
    private static ILogger Log => field ??= StaticLog.For(typeof(PttWakeHandler));

    public static void Handle(NotificationData data)
    {
        if (data.ChatId is not { } chatId || data.StartedAt is not { } startedAt) {
            Log.LogWarning("Invalid SpeechStarted push, message #{MessageId}", data.MessageId);
            return;
        }

        var isForeground = AndroidUtils.IsAppForeground() ?? false;
        // Before the FGS and the mic-blocked notification, both of which are themselves noise:
        // a silenced phone must stay silent. Foreground is exempt - the user is looking at the
        // app, so this is playback they asked for rather than an alert that interrupts them.
        if (!isForeground && AndroidRingerMode.IsSilenced) {
            Log.LogInformation("PTT wake for chat #{ChatId} suppressed: the phone is silenced", chatId);
            ShowFallbackNotification(chatId, silent: true);
            return;
        }

        var isServiceShown = false;
        if (!isForeground) {
            // First and synchronously: FGS start must land inside the FCM high-priority
            // exemption window; the service self-guards the 5s startForeground rule.
            isServiceShown = ShowForegroundService(chatId, InitialTitle);
        }
        // No activity has run in this process, so nothing can have started the service while the
        // app was visible - which is the only way Android hands out the while-in-use microphone.
        // This reply cannot record, and saying so now beats letting the user gesture into silence:
        // the notification's full-screen intent brings the app up over the keyguard, and that is
        // what earns the grant back.
        if (!MainActivity.HasEverRun) {
            Log.LogWarning("Wake in a process with no activity - the microphone can't be granted yet");
            MicrophoneBlockedNotification.ShowUnavailable();
        }

        try {
            BlazorWebViewApp.EnsureStarted();
            _ = BackgroundTask.Run(
                () => PttSession.HandleWake(chatId, startedAt, isForeground, AndroidPlatform.Instance),
                Log, "SpeechStarted wake failed", CancellationToken.None);
        }
        catch (Exception e) {
            // A synchronous throw here would otherwise leave the FGS up with nothing behind it.
            Log.LogError(e, "Couldn't start the wake for chat #{ChatId}", chatId);
            if (isServiceShown)
                AndroidPlatform.Instance.OnWakeFailed(chatId);
            throw;
        }
    }

    public static void StopHeadlessSession()
        => PttSession.StopHeadless(AndroidPlatform.Instance);

    // Private methods

    private static async Task UpdateForegroundServiceTitle(AppUIHub hub, ChatId chatId)
    {
        try {
            var chat = await hub.Chats.Get(hub.Session, chatId, CancellationToken.None).ConfigureAwait(false);
            if (chat is not null)
                ShowForegroundService(chatId, chat.Title);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't update the FGS title for chat #{ChatId}", chatId);
        }
    }

    private static bool ShowForegroundService(ChatId chatId, string title)
    {
        // A wake is proof this user is armed, and it's the one signal that reaches a process the
        // user hasn't opened yet - so the next launch can raise the mic-typed service from the
        // foreground without waiting for the widget to work the armed set out. See MainActivity.
        MauiPreferences.IsPttArmed = true;
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidActivitiesForegroundService));
        intent.SetAction(AndroidActivitiesForegroundService.ActionShow);
        intent.PutExtra(IntentExtras.Kind, (int)ActivityKind.Listening);
        intent.PutExtra(IntentExtras.ServiceTypes, (int)ActivityServiceTypes.Playback);
        intent.PutExtra(IntentExtras.ChatId, chatId.Value);
        intent.PutExtra(IntentExtras.ChatTitle, title);
        intent.PutExtra(IntentExtras.ChatPicUri, "");
        intent.PutExtra(IntentExtras.ExtraChatCount, 0);
        intent.PutExtra(IntentExtras.IsPaused, false);
        intent.PutExtra(IntentExtras.CanPause, true);
        // TryStart, not StartForegroundService: the fast-fail wake below stops the service before
        // OnStartCommand can run, and only the registered start defers that stop instead of
        // letting Android kill us with ForegroundServiceDidNotStartInTimeException.
        if (!AndroidActivitiesForegroundService.TryStart(context, intent))
            return false;

        AndroidActivitiesBackend.MarkForegroundServiceShown();
        return true;
    }

    private static void HideForegroundService(bool mustOwn = false)
    {
        // A wake failure must not take down a service the WebView widget has since taken over:
        // the widget's state doesn't change on our failure, so nothing would ever re-show it.
        if (mustOwn && !AndroidActivitiesBackend.IsForegroundServiceWakeOwned())
            return;

        AndroidActivitiesForegroundService.Stop(Platform.AppContext);
        AndroidActivitiesBackend.MarkForegroundServiceHidden();
    }

    private static void ShowFallbackNotification(ChatId chatId, bool silent = false)
        // Tagged by chat id, so a whole night of suppressed wakes collapses into one entry
        // per chat instead of stacking.
        => NotificationHelper.ShowChatNotification(
            chatId.Value,
            "Voxt",
            "Someone is talking in a chat you keep listening to",
            null,
            Links.Chat(chatId),
            silent: silent);

    // Nested types

    private sealed class AndroidPlatform : PttPlatform
    {
        public static readonly AndroidPlatform Instance = new();
        public override bool IsSilenced => AndroidRingerMode.IsSilenced;

        public override void OnWakeFailed(ChatId chatId)
        {
            ShowFallbackNotification(chatId);
            HideForegroundService(mustOwn: true);
        }

        public override void OnWakeIgnored(ChatId chatId, PttWakeIgnoreReason reason)
        {
            // A device with PTT switched off stays fully inert; the notification is for the
            // phone that would have played this if it weren't silenced.
            if (reason == PttWakeIgnoreReason.Silenced)
                ShowFallbackNotification(chatId, silent: true);

            HideForegroundService(mustOwn: true);
        }

        public override void OnHeadlessTeardown()
            => HideForegroundService();

        public override Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
            => UpdateForegroundServiceTitle(hub, chatId);
    }
}
