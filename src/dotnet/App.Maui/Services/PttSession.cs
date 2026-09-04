using ActualChat.Security;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Process-global PTT entry points: scope resolution (live WebView scope vs
/// <see cref="HeadlessBlazorScope"/>), app-ready waits, and headless-session teardown.
/// Per-scope work lives in <see cref="PttSessionCore"/>.
/// </summary>
public static class PttSession
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TeardownCheckPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopHotReplyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandOffHotReplyTimeout = TimeSpan.FromMinutes(2);
    private const int TeardownIdleChecks = 2;
    private static readonly Lock Lock = new();
    private static Task? _teardownWatcher;
    private static ILogger Log => field ??= StaticLog.For(typeof(PttSession));

    public static async Task HandleWake(
        ChatId chatId, Moment startedAt, bool isForeground, PttPlatform platform)
    {
        try {
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(StartupTimeout).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(StartupTimeout).ConfigureAwait(false);

            var (scopedServices, isHeadless) = await ResolveScope(true, CancellationToken.None).ConfigureAwait(false);
            var core = scopedServices.GetRequiredService<PttSessionCore>();
            var audioFocusDenialCount = core.AudioFocusDenialCount;
            var ignoreReason = await core.StartPlayback(chatId, startedAt, isForeground, isHeadless, platform)
                .ConfigureAwait(false);
            if (ignoreReason is { } reason) {
                platform.OnWakeIgnored(chatId, reason);
                if (isHeadless)
                    await StopAndDisposeCurrent($"PTT wake ignored: {reason}").ConfigureAwait(false);
                return;
            }

            if (isHeadless)
                EnsureTeardownWatcher(platform);
            if (!isForeground)
                core.WatchAudioFocus(
                    audioFocusDenialCount, chatId, platform,
                    () => StopAndDisposeCurrent("audio focus denied"));
        }
        catch (Exception e) {
            Log.LogError(e, "PTT wake failed for chat #{ChatId}", chatId);
            platform.OnWakeFailed(chatId);
            await StopAndDisposeCurrent("wake failed").ConfigureAwait(false);
        }
    }

    public static async Task<PttReply?> HandleTransmit(PttPlatform platform)
    {
        var isHeadless = false;
        try {
            // One budget for the whole boot, shared by all three waits: the mic-permission check
            // inside RequestReply cannot show a prompt from a locked screen, so nothing here may
            // outlive it.
            using var cts = new CancellationTokenSource(Constants.Audio.PttTransmitStartupTimeout);
            var app = await BlazorWebViewApp.WhenAppReady.WaitAsync(cts.Token).ConfigureAwait(false);
            var sessionResolver = app.Services.GetRequiredService<TrueSessionResolver>();
            await sessionResolver.SessionTask.WaitAsync(cts.Token).ConfigureAwait(false);

            // No wait for a booting WebView: the budget is short, and a reply opened headless
            // survives the handoff anyway - see HandOff.
            IServiceProvider scopedServices;
            (scopedServices, isHeadless) = await ResolveScope(false, cts.Token).ConfigureAwait(false);
            var core = scopedServices.GetRequiredService<PttSessionCore>();
            return await core.Transmit(isHeadless, platform, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) {
            // The boot didn't reach the scope, so there is no reply to close and no cue to play -
            // core.Transmit handles its own failures past that point.
            Log.LogWarning(e, "PTT transmit didn't fit into the startup budget");
            return null;
        }
        catch (Exception e) {
            Log.LogError(e, "PTT transmit failed");
            return null;
        }
        finally {
            // Also on every failure path: ResolveScope may have created this scope, and nothing
            // else would ever tear it down. The watcher disposes it only once it's idle.
            if (isHeadless)
                EnsureTeardownWatcher(platform);
        }
    }

    public static void HandOffHeadless(IServiceProvider webViewServices)
    {
        // Synchronous detach: from here every reader routes to the WebView scope, while what the
        // headless scope was doing moves over in the background.
        if (HeadlessBlazorScope.TryDetachCurrent("WebView scope published") is not { } headless)
            return;

        _ = BackgroundTask.Run(
            () => HandOff(headless, webViewServices), Log, "Headless scope handoff failed", CancellationToken.None);
    }

    public static void StopHeadless(PttPlatform platform)
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
        // The only disposal door for a headless scope: a scope may hold a hot PTT reply, and
        // disposing one out from under an open mic drops the entry with nothing recorded.
        try {
            var hub = scope.Services.GetRequiredService<AppUIHub>();
            if (!hub.ChatAudioUI.IsRecording())
                return;

            Log.LogInformation("Closing a hot PTT reply before disposing the headless scope");
            using var cts = new CancellationTokenSource(StopHotReplyTimeout);
            await scope.Services.GetRequiredService<PttSessionCore>()
                .StopReplyAndWaitForRecorder(cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't close the hot PTT reply before disposing the headless scope");
        }
        finally {
            onStopped?.Invoke();
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Private methods

    private static async Task<(IServiceProvider Services, bool IsHeadless)> ResolveScope(
        bool mayWaitForWebViewScope, CancellationToken cancellationToken)
    {
        if (AppServicesAccessor.TryGetScopedServices(out var liveScope))
            return (liveScope, false);

        if (mayWaitForWebViewScope && IsWebViewScopeExpected()) {
            // A headless scope started now would only have to be handed off in a few seconds -
            // see HandOffHeadless - and each handoff costs the listener a buffer of audio.
            // WaitAsync rather than a token: the inner wait sits on a non-cancellable source.
            await AppServicesAccessor.WhenBlazorAppServicesReady(cancellationToken)
                .WaitAsync(Constants.Audio.PttWebViewScopeWaitTimeout, cancellationToken)
                .SilentAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (AppServicesAccessor.TryGetScopedServices(out liveScope))
                return (liveScope, false);

            Log.LogWarning("The WebView scope didn't arrive in time, falling back to a headless scope");
        }
        if (HeadlessBlazorScope.GetOrCreate() is { } headless)
            return (headless.Services, true);
        if (AppServicesAccessor.TryGetScopedServices(out liveScope!))
            // Lost the creation race to a just-published WebView scope
            return (liveScope, false);

        throw StandardError.Internal("No service scope is available.");
    }

    private static bool IsWebViewScopeExpected()
    {
        // A dead WebView is recreated only by the next navigation, which nothing headless drives.
        if (MauiWebView.Current is { IsDead: true })
            return false;

#if ANDROID
        // The Activity is what creates the WebView; MauiWebView.Current alone may be the
        // disconnected one a destroyed Activity left behind, and MainPage outlives both.
        return MainActivity.HasCurrent;
#else
        return MainPage.Current is not null;
#endif
    }

    private static async Task HandOff(HeadlessBlazorScope headless, IServiceProvider webViewServices)
    {
        // Two things the WebView scope can't work out on its own: what was being listened to
        // (its InitializeListening re-arms the armed set, but players start only after Enable,
        // which a background WebView may never reach), and the answer window, which Android
        // doesn't persist. The catch-up anchor deliberately stays behind: the new player joins
        // at the live edge, a copied anchor would replay the utterance from its start.
        var headlessHub = headless.Services.GetRequiredService<AppUIHub>();
        var listeningChatIds = await headlessHub.ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
        var lastIncomingVoiceAt = headlessHub.IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        // The headless players stop before the WebView ones start: a gap of one buffer beats two
        // players. Listening only - a hot reply keeps recording, see below.
        await headlessHub.ChatAudioUI.ClearListeningChats().ConfigureAwait(false);

        var scopedServices = await AppServicesAccessor.WhenBlazorAppServicesReady()
            .WaitAsync(StartupTimeout)
            .ConfigureAwait(false);
        if (ReferenceEquals(scopedServices, webViewServices))
            await Resume(scopedServices, listeningChatIds, lastIncomingVoiceAt).ConfigureAwait(false);

        if (headlessHub.ChatAudioUI.IsRecording()) {
            // An Apple PTT Talk press on a killed app boots the WebView while the reply it
            // opened is still recording, and closing the mic here would cut that very reply.
            // The WebView scope can't compete for the mic: ActiveChatsUI.FixStoredActiveChats
            // drops a stored recording on start.
            Log.LogInformation("PTT: a hot reply keeps the headless scope alive until it closes");
            using var cts = new CancellationTokenSource(HandOffHotReplyTimeout);
            var cRecordingChatId = await Computed
                .Capture(() => headlessHub.ChatAudioUI.GetRecordingChatId())
                .ConfigureAwait(false);
            await cRecordingChatId.When(x => x is null, cts.Token).SilentAwait(false);
        }
        await StopAndDispose(headless).ConfigureAwait(false);
    }

    private static async Task Resume(
        IServiceProvider scopedServices,
        ImmutableHashSet<ChatId> listeningChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt)
    {
        var hub = scopedServices.GetRequiredService<AppUIHub>();
        foreach (var (chatId, at) in lastIncomingVoiceAt)
            hub.IncomingVoiceActivityUI.NoteIncomingVoice(chatId, at);
        if (listeningChatIds.IsEmpty)
            return;

        Log.LogInformation(
            "PTT: handing {Count} listening chat(s) off to the WebView scope", listeningChatIds.Count);
        // A SetListeningState landing before the stored active chats are read would make
        // StoredState discard them.
        await hub.ActiveChatsUI.WhenReady.ConfigureAwait(false);
        hub.ChatAudioUI.Enable();
        foreach (var chatId in listeningChatIds)
            await hub.ChatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
    }

    private static void EnsureTeardownWatcher(PttPlatform platform)
    {
        lock (Lock)
            _teardownWatcher ??= BackgroundTask.Run(
                () => WatchTeardown(platform), Log, "Teardown watcher failed", CancellationToken.None);
    }

    private static async Task WatchTeardown(PttPlatform platform)
    {
        try {
            var idleChecks = 0;
            while (true) {
                await Task.Delay(TeardownCheckPeriod).ConfigureAwait(false);
                if (AppServicesAccessor.TryGetScopedServices(out var liveScope)) {
                    // Normally already done by MauiWebView.SetScopedServices; a no-op then
                    HandOffHeadless(liveScope);
                    return;
                }
                if (HeadlessBlazorScope.Current is not { } headless)
                    return;

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

                Log.LogInformation("PTT: headless session is idle, tearing down");
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
