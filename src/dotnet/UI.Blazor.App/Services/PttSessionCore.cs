using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Scoped PTT session core: playback start, transmit, and reply teardown over
/// this scope's services. Scope resolution and the process-global teardown watcher live
/// in App.Maui's static PttSession facade.
/// </summary>
public sealed class PttSessionCore(AppUIHub hub) : IDisposable
{
    private static readonly TimeSpan AudioFocusCheckPeriod = TimeSpan.FromSeconds(0.5);
    private const int AudioFocusChecks = 10;
    private readonly CancellationTokenSource _disposeCts = new();
    private AppUIHub Hub { get; } = hub;
    private ILogger Log => field ??= Hub.Services.LogFor(GetType());
    public int AudioFocusDenialCount => Hub.ChatAudioUI.AudioFocusDenialCount;

    public void Dispose()
        => _disposeCts.CancelAndDisposeSilently();

    public async Task<PttWakeIgnoreReason?> StartPlayback(
        ChatId chatId,
        Moment startedAt,
        bool isForeground,
        bool isHeadless,
        PttPlatform platform)
    {
        var chatAudioUI = Hub.ChatAudioUI;
        // Second gate behind the server-side fan-out filter: a wake that reaches a device whose
        // registration is stale (or that raced a toggle) must stay inert when PTT is off here.
        if (!await chatAudioUI.IsPttEnabledOnDevice(CancellationToken.None).ConfigureAwait(false)) {
            Log.LogWarning("PTT wake for chat #{ChatId} ignored: PTT is off on this device", chatId);
            return PttWakeIgnoreReason.DeviceDisabled;
        }
        // Re-checked here as well as at the platform entry point: the phone can be silenced
        // between the push and this boot. Foreground is exempt - the user is looking at the app,
        // so this is playback they asked for rather than an alert that interrupts them.
        if (!isForeground && platform.IsSilenced) {
            Log.LogInformation("PTT wake for chat #{ChatId} ignored: the phone is silenced", chatId);
            return PttWakeIgnoreReason.Silenced;
        }

        if (isHeadless)
            chatAudioUI.IsPttHeadless = true;
        chatAudioUI.Enable();
        // Fire-and-forget: ServerTimeSync's own loop only starts syncing after a 3s settling delay,
        // and everything downstream that wants server time would sit behind it.
        if (Hub.Services.GetService<ServerTimeSync>() is { } serverTimeSync)
            _ = BackgroundTask.Run(
                () => serverTimeSync.EnsureSynced(CancellationToken.None),
                Log, "Couldn't sync the server clock on wake", CancellationToken.None);
        // The utterance may be over by the time we boot, so HasIncomingVoice would never see an edge
        // for it and the answer window would never open.
        Hub.IncomingVoiceActivityUI.NoteIncomingVoice(
            chatId, Ptt.GetWakeAnswerStamp(startedAt, Hub.Clocks.ServerClock.Now));

        if (isForeground) {
            // The user is in the app: don't hijack their state with a forced replay -
            // just make sure the trigger chat is being listened to.
            await chatAudioUI.SetListeningState(chatId, true).ConfigureAwait(false);
            await platform.OnForegroundWakeHandled(chatId).ConfigureAwait(false);
            return null;
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
        if (!Ptt.IsStaleWake(startedAt, Hub.Clocks.SystemClock.Now))
            chatAudioUI.SetListeningCatchUp(chatId, startedAt);
        foreach (var armedChatId in armedChatIds)
            await chatAudioUI.SetListeningState(armedChatId, true).ConfigureAwait(false);

        _ = platform.OnPlaybackStarted(Hub, chatId);
        return null;
    }

    public async Task<PttReply?> Transmit(
        bool isHeadless, PttPlatform platform, CancellationToken cancellationToken)
    {
        PttReply? reply = null;
        try {
            if (isHeadless)
                Hub.ChatAudioUI.IsPttHeadless = true;

            // Rehearsing in Settings must never transmit, and RequestReply is idempotent, so a
            // gesture-opened mic would make it report success and the transmission would later
            // close a reply it never started.
            var isPracticeMode = Hub.GestureUI.IsPracticeMode;
            var recordingChatId = isPracticeMode ? null
                : await Hub.ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
            if (!Ptt.MayTransmit(isPracticeMode, recordingChatId))
                return null;

            // The live signal can't have fired for an utterance that ended before this process
            // booted, so the persisted wake is the only thing that opens an answer window for it.
            if (platform.LastWake is { } lastWake)
                Hub.IncomingVoiceActivityUI.NoteIncomingVoice(lastWake.ChatId, lastWake.At);

            // Null unless this very call opened the mic - see PttReplyUI.RequestReply.
            reply = await Hub.PttReplyUI
                .RequestReply(ReplyTargetResolver.UnboundedRecencyWindow, cancellationToken)
                .ConfigureAwait(false);
            return reply;
        }
        catch (OperationCanceledException e) {
            // Expected degraded mode: the boot budget ran out - see PttTransmitStartupTimeout.
            Log.LogWarning(e, "PTT transmit didn't fit into the startup budget");
            await StopOrphanedReply(reply).ConfigureAwait(false);
            PlayFailureCue();
            return null;
        }
        catch (Exception e) {
            Log.LogError(e, "PTT transmit failed");
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
        await Hub.PttReplyUI.StopReply().ConfigureAwait(false);
        await Hub.AudioRecorder.State.Computed
            .When(x => x.ChatId is null, cancellationToken)
            .ConfigureAwait(false);
    }

    public void WatchAudioFocus(
        int baselineDenialCount, ChatId chatId, PttPlatform platform, Func<Task> onDenied)
        => _ = BackgroundTask.Run(async () => {
            // A denial drops replay state and pauses listening playback, and nothing throws -
            // so the wake would end in silence instead of a fallback.
            try {
                for (var i = 0; i < AudioFocusChecks; i++) {
                    await Task.Delay(AudioFocusCheckPeriod, _disposeCts.Token).ConfigureAwait(false);
                    if (Hub.ChatAudioUI.AudioFocusDenialCount == baselineDenialCount)
                        continue;

                    Log.LogWarning("PTT wake was denied audio focus for chat #{ChatId}", chatId);
                    platform.OnWakeFailed(chatId);
                    await onDenied.Invoke().ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) { }
        }, Log, "Audio focus watch failed", CancellationToken.None);

    // Private methods

    private async Task StopOrphanedReply(PttReply? reply)
    {
        // Identity, not "is anything recording": StopReply(reply) no-ops unless this is still the
        // open reply, so a gesture that grabbed the mic meanwhile keeps it.
        if (reply is null)
            return;

        try {
            Log.LogWarning("Closing the reply opened by a failed PTT transmit");
            await Hub.PttReplyUI.StopReply(reply).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't close the reply opened by a failed PTT transmit");
        }
    }

    private void PlayFailureCue()
        // Fire-and-forget: tune playback must not block the caller or delay teardown watcher.
        => _ = BackgroundTask.Run(
            () => Hub.PttReplyUI.PlayFailureCue(),
            Log, "Couldn't play the PTT transmit failure cue", CancellationToken.None);
}
