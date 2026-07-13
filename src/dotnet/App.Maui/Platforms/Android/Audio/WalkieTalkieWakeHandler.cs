using ActualChat.App.Maui.Services;
using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using IntentExtras = ActualChat.App.Maui.Audio.AndroidAudioWidgetForegroundService.IntentExtras;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Handles kind=SpeechStarted FCM wakes: starts the audio FGS, boots the app container
/// headlessly when no WebView scope exists, and replays the utterance from its start.
/// </summary>
public static class WalkieTalkieWakeHandler
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private const int TeardownIdleChecks = 2;
    private static readonly Lock Lock = new();
    private static Task? _teardownWatcher;
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
            () => HandleImpl(chatId, startedAt, isForeground),
            Log, "SpeechStarted wake failed", CancellationToken.None);
    }

    public static void StopHeadlessSession()
        => _ = BackgroundTask.Run(async () => {
            if (HeadlessBlazorScope.Current is not { } headless)
                return;

            var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
            chatAudioUI.StopReplay();
            await chatAudioUI.ClearListeningChats().ConfigureAwait(false);
            HideForegroundService();
            await HeadlessBlazorScope.DisposeCurrent("stopped from the notification").ConfigureAwait(false);
        }, Log, "StopHeadlessSession failed", CancellationToken.None);

    // Private methods

    private static async Task HandleImpl(ChatId chatId, Moment startedAt, bool isForeground)
    {
        try {
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(StartupTimeout).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(StartupTimeout).ConfigureAwait(false);

            IServiceProvider scopedServices;
            var isHeadless = false;
            if (AppServicesAccessor.TryGetScopedServices(out var liveScope))
                scopedServices = liveScope;
            else if (HeadlessBlazorScope.GetOrCreate() is { } headless) {
                scopedServices = headless.Services;
                isHeadless = true;
            }
            else if (AppServicesAccessor.TryGetScopedServices(out liveScope!))
                // Lost the creation race to a just-published WebView scope
                scopedServices = liveScope;
            else
                throw StandardError.Internal("No service scope is available.");

            await StartPlayback(scopedServices, chatId, startedAt, isForeground, isHeadless)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher();
        }
        catch (Exception e) {
            Log.LogError(e, "SpeechStarted wake failed for chat #{ChatId}", chatId);
            ShowFallbackNotification(chatId);
            HideForegroundService();
            await HeadlessBlazorScope.DisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    private static async Task StartPlayback(
        IServiceProvider scopedServices, ChatId chatId, Moment startedAt, bool isForeground, bool isHeadless)
    {
        var hub = scopedServices.GetRequiredService<AppUIHub>();
        var chatAudioUI = hub.ChatAudioUI;
        if (isHeadless)
            chatAudioUI.IsWalkieTalkieHeadless = true;
        chatAudioUI.Enable();

        // The server gates wakes on the same settings; re-read them for the restore set.
        var restoreSet = await chatAudioUI.GetChatsYouNeedToKeepListeningTo(CancellationToken.None)
            .ConfigureAwait(false);
        if (!restoreSet.Contains(chatId))
            restoreSet = [..restoreSet, chatId];

        if (!isForeground) {
            // The replay path bypasses ChatListeningPlayer, which normally plays this cue on
            // stream-start after a long lull - so the wake plays it explicitly.
            _ = hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);
        }

        if (WalkieTalkie.IsStaleWake(startedAt, hub.Clocks.SystemClock.Now))
            foreach (var armedChatId in restoreSet)
                await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);
        else
            await chatAudioUI.StartWalkieTalkieReplay(chatId, startedAt, restoreSet).ConfigureAwait(false);

        if (!isForeground)
            _ = UpdateForegroundServiceTitle(hub, chatId);
    }

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

    private static void EnsureTeardownWatcher()
    {
        lock (Lock)
            _teardownWatcher ??= BackgroundTask.Run(
                WatchTeardown, Log, "Teardown watcher failed", CancellationToken.None);
    }

    private static async Task WatchTeardown()
    {
        try {
            var idleChecks = 0;
            while (true) {
                await Task.Delay(TeardownCheckPeriod).ConfigureAwait(false);
                if (HeadlessBlazorScope.Current is not { } headless)
                    return; // The WebView scope owns audio now; its AudioWidget owns the FGS

                var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
                var listeningChatIds = await chatAudioUI.GetListeningChatIds().ConfigureAwait(false);
                if (!listeningChatIds.IsEmpty || chatAudioUI.ReplayState.Value is not null) {
                    idleChecks = 0;
                    continue;
                }

                // Two consecutive idle checks: the replay-ended -> listening-restored transition
                // has a short gap that must not read as "session over".
                if (++idleChecks < TeardownIdleChecks)
                    continue;

                Log.LogInformation("Walkie-talkie: headless session is idle, tearing down");
                HideForegroundService();
                await HeadlessBlazorScope.DisposeCurrent("armed (idle)").ConfigureAwait(false);
                return;
            }
        }
        finally {
            lock (Lock)
                _teardownWatcher = null;
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
}
