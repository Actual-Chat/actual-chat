using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Android shell for walkie-talkie wakes: FGS lifecycle + FCM entry point;
/// the portable core lives in <see cref="WalkieTalkieSession"/>.
/// </summary>
public static class WalkieTalkieWakeHandler
{
    private static ILogger Log => field ??= StaticLog.For(typeof(WalkieTalkieWakeHandler));

    public static void Handle(NotificationData data)
    {
        if (data.ChatId is not { } chatId || data.StartedAt is not { } startedAt) {
            Log.LogWarning("Invalid SpeechStarted push, message #{MessageId}", data.MessageId);
            return;
        }

        var isForeground = AndroidUtils.IsAppForeground() ?? false;
        if (!isForeground)
            try {
                // First and synchronously: FGS start must land inside the FCM high-priority
                // exemption window; the service self-guards the 5s startForeground rule.
                ShowForegroundService(chatId, "Listening…");
            }
            catch (Exception e) {
                // Denied FGS start (OEM restrictions etc.) must not kill the wake:
                // playback is still attempted, and any later failure shows the fallback.
                Log.LogWarning(e, "Couldn't start the audio FGS for chat #{ChatId}", chatId);
            }
        BlazorWebViewApp.EnsureStarted();
        _ = BackgroundTask.Run(
            () => WalkieTalkieSession.HandleWake(chatId, startedAt, isForeground, AndroidPlatform.Instance),
            Log, "SpeechStarted wake failed", CancellationToken.None);
    }

    public static void StopHeadlessSession()
        => WalkieTalkieSession.StopHeadless(AndroidPlatform.Instance);

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

    private static void ShowForegroundService(ChatId chatId, string title)
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        intent.SetAction(AndroidAudioWidgetForegroundService.ActionShow);
        intent.PutExtra(IntentExtras.Mode, (int)AudioWidgetMode.Listening);
        intent.PutExtra(IntentExtras.ChatId, chatId.Value);
        intent.PutExtra(IntentExtras.ChatTitle, title);
        intent.PutExtra(IntentExtras.ChatPicUri, "");
        intent.PutExtra(IntentExtras.ExtraChatCount, 0);
        intent.PutExtra(IntentExtras.IsPaused, false);
        context.StartForegroundService(intent);
        AndroidAudioWidget.MarkForegroundServiceShown();
    }

    private static void HideForegroundService()
    {
        var context = Platform.AppContext;
        var intent = new Intent(context, typeof(AndroidAudioWidgetForegroundService));
        context.StopService(intent);
        AndroidAudioWidget.MarkForegroundServiceHidden();
    }

    private static void ShowFallbackNotification(ChatId chatId)
        => NotificationHelper.ShowChatNotification(
            chatId.Value,
            "Voxt",
            "Someone is talking in a chat you keep listening to",
            null,
            Links.Chat(chatId),
            silent: false);

    // Nested types

    private sealed class AndroidPlatform : WalkieTalkiePlatform
    {
        public static readonly AndroidPlatform Instance = new();

        public override void OnWakeFailed(ChatId chatId)
        {
            ShowFallbackNotification(chatId);
            HideForegroundService();
        }

        public override void OnHeadlessTeardown()
            => HideForegroundService();

        public override Task OnPlaybackStarted(AppUIHub hub, ChatId chatId)
            => UpdateForegroundServiceTitle(hub, chatId);
    }
}
