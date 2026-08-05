using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Process-global walkie-talkie entry points: scope resolution (live WebView scope vs
/// <see cref="HeadlessBlazorScope"/>), app-ready waits, and headless-session teardown.
/// Per-scope work lives in <see cref="WalkieTalkieSessionCore"/>.
/// </summary>
public static class WalkieTalkieSession
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopHotReplyTimeout = TimeSpan.FromSeconds(5);
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

            var (scopedServices, isHeadless) = ResolveScope();
            var core = scopedServices.GetRequiredService<WalkieTalkieSessionCore>();
            var audioFocusDenialCount = core.AudioFocusDenialCount;
            await core.StartPlayback(chatId, startedAt, isForeground, isHeadless, platform)
                .ConfigureAwait(false);
            if (isHeadless)
                EnsureTeardownWatcher(platform);
            if (!isForeground)
                core.WatchAudioFocus(
                    audioFocusDenialCount, chatId, platform,
                    () => StopAndDisposeCurrent("audio focus denied"));
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie wake failed for chat #{ChatId}", chatId);
            platform.OnWakeFailed(chatId);
            await StopAndDisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    public static async Task<WalkieTalkieReply?> HandleTransmit(WalkieTalkiePlatform platform)
    {
        var isHeadless = false;
        try {
            // One budget for the whole boot, shared by all three waits: the mic-permission check
            // inside RequestReply cannot show a prompt from a locked screen, so nothing here may
            // outlive it.
            using var cts = new CancellationTokenSource(Constants.Audio.WalkieTalkiePttTransmitStartupTimeout);
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(cts.Token).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(cts.Token).ConfigureAwait(false);

            IServiceProvider scopedServices;
            (scopedServices, isHeadless) = ResolveScope();
            var core = scopedServices.GetRequiredService<WalkieTalkieSessionCore>();
            return await core.Transmit(isHeadless, platform, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) {
            // The boot didn't reach the scope, so there is no reply to close and no cue to play -
            // core.Transmit handles its own failures past that point.
            Log.LogWarning(e, "Walkie-talkie transmit didn't fit into the startup budget");
            return null;
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie transmit failed");
            return null;
        }
        finally {
            // Also on every failure path: ResolveScope may have created this scope, and nothing
            // else would ever tear it down. The watcher disposes it only once it's idle.
            if (isHeadless)
                EnsureTeardownWatcher(platform);
        }
    }

    public static void StopHeadless(WalkieTalkiePlatform platform)
        => _ = BackgroundTask.Run(async () => {
            if (HeadlessBlazorScope.TryDetachCurrent("stopped by the user") is not { } headless)
                return;

            var chatAudioUI = headless.Services.GetRequiredService<AppUIHub>().ChatAudioUI;
            chatAudioUI.StopReplay();
            await chatAudioUI.ClearListeningChats().ConfigureAwait(false);
            // The teardown runs between the mic close and the disposal: it stops the mic-typed
            // foreground service, and doing that first would revoke mic access mid-close.
            await StopAndDispose(headless, platform.OnHeadlessTeardown).ConfigureAwait(false);
        }, Log, "StopHeadless failed", CancellationToken.None);

    public static Task StopAndDisposeCurrent(string reason)
        => HeadlessBlazorScope.TryDetachCurrent(reason) is { } scope
            ? StopAndDispose(scope)
            : Task.CompletedTask;

    public static async Task StopAndDispose(HeadlessBlazorScope scope, Action? onStopped = null)
    {
        // The only disposal door for a headless scope: a scope may hold a hot walkie reply, and
        // disposing one out from under an open mic drops the entry with nothing recorded.
        try {
            var hub = scope.Services.GetRequiredService<AppUIHub>();
            if (!hub.ChatAudioUI.IsRecording())
                return;

            Log.LogInformation("Closing a hot walkie reply before disposing the headless scope");
            using var cts = new CancellationTokenSource(StopHotReplyTimeout);
            await scope.Services.GetRequiredService<WalkieTalkieSessionCore>()
                .StopReplyAndWaitForRecorder(cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't close the hot walkie reply before disposing the headless scope");
        }
        finally {
            onStopped?.Invoke();
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Private methods

    private static (IServiceProvider Services, bool IsHeadless) ResolveScope()
    {
        if (AppServicesAccessor.TryGetScopedServices(out var liveScope))
            return (liveScope, false);
        if (HeadlessBlazorScope.GetOrCreate() is { } headless)
            return (headless.Services, true);
        if (AppServicesAccessor.TryGetScopedServices(out liveScope!))
            // Lost the creation race to a just-published WebView scope
            return (liveScope, false);

        throw StandardError.Internal("No service scope is available.");
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
                // A transmit into a headless scope with nothing playing looks exactly like an idle
                // session - without this the watcher would dispose the scope under an open mic.
                if (chatAudioUI.IsRecording()) {
                    idleChecks = 0;
                    continue;
                }

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
                await StopAndDisposeCurrent("armed (idle)").ConfigureAwait(false);
                return;
            }
        }
        finally {
            lock (Lock)
                _teardownWatcher = null;
        }
    }
}
