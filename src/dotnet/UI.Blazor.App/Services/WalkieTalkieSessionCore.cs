using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped walkie-talkie session core: playback start, transmit, and reply teardown over
/// this scope's services. Scope resolution and the process-global teardown watcher live
/// in App.Maui's static WalkieTalkieSession facade.
/// </summary>
public sealed class WalkieTalkieSessionCore(AppUIHub hub) : IDisposable
{
    private static readonly TimeSpan AudioFocusCheckPeriod = TimeSpan.FromSeconds(0.5);
    private const int AudioFocusChecks = 10;
    private readonly CancellationTokenSource _disposeCts = new();
    private AppUIHub Hub { get; } = hub;
    private ILogger Log => field ??= Hub.Services.LogFor(GetType());
    public int AudioFocusDenialCount => Hub.ChatAudioUI.AudioFocusDenialCount;

    public void Dispose()
        => _disposeCts.CancelAndDisposeSilently();

    public async Task StartPlayback(
        ChatId chatId,
        Moment startedAt,
        bool isForeground,
        bool isHeadless,
        WalkieTalkiePlatform platform)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        if (isHeadless)
            chatAudioUI.IsWalkieTalkieHeadless = true;
        chatAudioUI.Enable();
        // Fire-and-forget: ServerTimeSync's own loop only starts syncing after a 3s settling delay,
        // and everything downstream that wants server time would sit behind it.
        if (Hub.Services.GetService<ServerTimeSync>() is { } serverTimeSync)
            _ = BackgroundTask.Run(
                () => serverTimeSync.EnsureSynced(CancellationToken.None),
                Log, "Couldn't sync the server clock on wake", CancellationToken.None);
        // The utterance may be over by the time we boot, so HasIncomingVoice would never see an edge
        // for it and the answer window would never open.
        Hub.IncomingVoiceActivityUI.NoteIncomingVoice(chatId, startedAt);

        if (isForeground) {
            // The user is in the app: don't hijack their state with a forced replay -
            // just make sure the trigger chat is being listened to.
            await chatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            await platform.OnForegroundWakeHandled(chatId).ConfigureAwait(false);
            return;
        }

        // The cold boot outlives ChatListeningPlayer's stream-start cue window, so the wake
        // plays it explicitly.
        _ = Hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);

        // The server gates wakes on the same settings; re-read them for the armed set.
        var armedChatIds = await chatAudioUI.GetChatsYouNeedToKeepListeningTo(CancellationToken.None)
            .ConfigureAwait(false);
        if (!armedChatIds.Contains(chatId))
            armedChatIds = [..armedChatIds, chatId];

        // A fresh wake joins the trigger utterance from its start (it's still streaming or just
        // ended); a stale one goes straight to live listening.
        if (!WalkieTalkie.IsStaleWake(startedAt, Hub.Clocks.SystemClock.Now))
            chatAudioUI.SetListeningCatchUp(chatId, startedAt);
        foreach (var armedChatId in armedChatIds)
            await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);

        _ = platform.OnPlaybackStarted(Hub, chatId);
    }

    public async Task<WalkieTalkieReply?> Transmit(
        bool isHeadless, WalkieTalkiePlatform platform, CancellationToken cancellationToken)
    {
        WalkieTalkieReply? reply = null;
        try {
            if (isHeadless)
                Hub.ChatAudioUI.IsWalkieTalkieHeadless = true;

            // Rehearsing in Settings must never transmit, and RequestReply is idempotent, so a
            // gesture-opened mic would make it report success and the transmission would later
            // close a reply it never started.
            var isPracticeMode = Hub.GestureUI.IsPracticeMode;
            var recordingChatId = isPracticeMode ? null
                : await Hub.ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
            if (!WalkieTalkie.MayTransmit(isPracticeMode, recordingChatId))
                return null;

            // The live signal can't have fired for an utterance that ended before this process
            // booted, so the persisted wake is the only thing that opens an answer window for it.
            if (platform.LastWake is { } lastWake)
                Hub.IncomingVoiceActivityUI.NoteIncomingVoice(lastWake.ChatId, lastWake.At);

            // Null unless this very call opened the mic - see WalkieTalkieReplyUI.RequestReply.
            reply = await Hub.WalkieTalkieReplyUI
                .RequestReply(ReplyTargetResolver.UnboundedRecencyWindow, cancellationToken)
                .ConfigureAwait(false);
            return reply;
        }
        catch (OperationCanceledException e) {
            // Expected degraded mode: the boot budget ran out - see WalkieTalkiePttTransmitStartupTimeout.
            Log.LogWarning(e, "Walkie-talkie transmit didn't fit into the startup budget");
            await StopOrphanedReply(reply).ConfigureAwait(false);
            PlayFailureCue();
            return null;
        }
        catch (Exception e) {
            Log.LogError(e, "Walkie-talkie transmit failed");
            await StopOrphanedReply(reply).ConfigureAwait(false);
            PlayFailureCue();
            return null;
        }
    }

    public async Task StopReplyAndWaitForRecorder(CancellationToken cancellationToken)
    {
        // StopReply only writes the "stop recording" intent; RecordChat's own teardown (a
        // different async flow in the same scope) is what actually closes the mic and plays the
        // cue. Waiting for ChatId to clear - IsRecording stays false while the engine starts -
        // is what turns this into a real stop-then-dispose rather than a race with teardown.
        await Hub.WalkieTalkieReplyUI.StopReply().ConfigureAwait(false);
        await Hub.AudioRecorder.State.Computed
            .When(x => x.ChatId is null, cancellationToken)
            .ConfigureAwait(false);
    }

    public void WatchAudioFocus(
        int baselineDenialCount, ChatId chatId, WalkieTalkiePlatform platform, Func<Task> onDenied)
        => _ = BackgroundTask.Run(async () => {
            // A denial makes ChatAudioUI.StateSync drop the replay/listening state it was just
            // given, and nothing throws - so the wake would end in silence instead of a fallback.
            try {
                for (var i = 0; i < AudioFocusChecks; i++) {
                    await Task.Delay(AudioFocusCheckPeriod, _disposeCts.Token).ConfigureAwait(false);
                    if (Hub.ChatAudioUI.AudioFocusDenialCount == baselineDenialCount)
                        continue;

                    Log.LogWarning("Walkie-talkie wake was denied audio focus for chat #{ChatId}", chatId);
                    platform.OnWakeFailed(chatId);
                    await onDenied.Invoke().ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) { }
        }, Log, "Audio focus watch failed", CancellationToken.None);

    // Private methods

    private async Task StopOrphanedReply(WalkieTalkieReply? reply)
    {
        // Identity, not "is anything recording": StopReply(reply) no-ops unless this is still the
        // open reply, so a gesture that grabbed the mic meanwhile keeps it.
        if (reply is null)
            return;

        try {
            Log.LogWarning("Closing the reply opened by a failed walkie-talkie transmit");
            await Hub.WalkieTalkieReplyUI.StopReply(reply).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't close the reply opened by a failed walkie-talkie transmit");
        }
    }

    private void PlayFailureCue()
        // Fire-and-forget: this reads a Kvas setting, and awaiting that on a timed-out boot would
        // leak the headless scope by delaying the caller's teardown watcher.
        => _ = BackgroundTask.Run(
            () => Hub.WalkieTalkieReplyUI.PlayFailureCue(),
            Log, "Couldn't play the walkie-talkie transmit failure cue", CancellationToken.None);
}
