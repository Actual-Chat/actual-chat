using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform-neutral walkie-talkie wake core: scope resolution (live WebView scope vs
/// <see cref="HeadlessBlazorScope"/>), playback start, and headless-session teardown.
/// </summary>
public static class WalkieTalkieSession
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private const int TeardownIdleChecks = 2;
    private static readonly Lock Lock = new();
    private static Task? _teardownWatcher;
    private static ILogger Log => field ??= StaticLog.For(typeof(WalkieTalkieSession));

    public static async Task HandleWake(
        ChatId chatId, Moment startedAt, bool isForeground, WalkieTalkiePlatform platform)
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

            await StartPlayback(scopedServices, chatId, startedAt, isForeground, isHeadless, platform)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher(platform);
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie wake failed for chat #{ChatId}", chatId);
            platform.OnWakeFailed(chatId);
            await HeadlessBlazorScope.DisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    public static void StopHeadless(WalkieTalkiePlatform platform)
        => _ = BackgroundTask.Run(async () => {
            if (HeadlessBlazorScope.Current is not { } headless)
                return;

            var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
            chatAudioUI.StopReplay();
            await chatAudioUI.ClearListeningChats().ConfigureAwait(false);
            platform.OnHeadlessTeardown();
            await HeadlessBlazorScope.DisposeCurrent("stopped by the user").ConfigureAwait(false);
        }, Log, "StopHeadless failed", CancellationToken.None);

    // Private methods

    private static async Task StartPlayback(
        IServiceProvider scopedServices,
        ChatId chatId,
        Moment startedAt,
        bool isForeground,
        bool isHeadless,
        WalkieTalkiePlatform platform)
    {
        var hub = scopedServices.GetRequiredService<AppUIHub>();
        var chatAudioUI = hub.ChatAudioUI;
        if (isHeadless)
            chatAudioUI.IsWalkieTalkieHeadless = true;
        chatAudioUI.Enable();

        if (isForeground) {
            // The user is in the app: don't hijack their state with a forced replay -
            // just make sure the trigger chat is being listened to.
            await chatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            await platform.OnForegroundWakeHandled(chatId).ConfigureAwait(false);
            return;
        }

        // The replay path bypasses ChatListeningPlayer, which normally plays this cue on
        // stream-start after a long lull - so the wake plays it explicitly.
        _ = hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);

        // The server gates wakes on the same settings; re-read them for the restore set.
        var restoreSet = await chatAudioUI.GetChatsYouNeedToKeepListeningTo(CancellationToken.None)
            .ConfigureAwait(false);
        if (!restoreSet.Contains(chatId))
            restoreSet = [..restoreSet, chatId];

        if (WalkieTalkie.IsStaleWake(startedAt, hub.Clocks.SystemClock.Now))
            foreach (var armedChatId in restoreSet)
                await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);
        else
            await chatAudioUI.StartWalkieTalkieReplay(chatId, startedAt, restoreSet).ConfigureAwait(false);

        _ = platform.OnPlaybackStarted(hub, chatId);
    }

    private static void EnsureTeardownWatcher(WalkieTalkiePlatform platform)
    {
        lock (Lock)
            _teardownWatcher ??= BackgroundTask.Run(
                () => WatchTeardown(platform), Log, "Teardown watcher failed", CancellationToken.None);
    }

    private static async Task WatchTeardown(WalkieTalkiePlatform platform)
    {
        try {
            var idleChecks = 0;
            while (true) {
                await Task.Delay(TeardownCheckPeriod).ConfigureAwait(false);
                if (HeadlessBlazorScope.Current is not { } headless)
                    return; // The WebView scope owns audio now

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
                platform.OnHeadlessTeardown();
                await HeadlessBlazorScope.DisposeCurrent("armed (idle)").ConfigureAwait(false);
                return;
            }
        }
        finally {
            lock (Lock)
                _teardownWatcher = null;
        }
    }
}
