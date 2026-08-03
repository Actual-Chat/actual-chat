using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Coordinates a walkie-talkie voice reply: resolves the target chat, opens the hot mic,
/// and runs a cold-start dead-man switch that closes the mic if no voice is heard in time.
/// The on-screen trigger and native triggers drive it via <see cref="RequestReply"/>/<see cref="StopReply"/>.
/// </summary>
public sealed class WalkieTalkieReplyUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly object _lock = new();
    private CancellationTokenSource? _coldStartCts;
    private bool _everVoiced;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;

    public static bool ShouldColdClose(bool everVoiced, TimeSpan elapsed, TimeSpan coldTimeout)
        => !everVoiced && elapsed >= coldTimeout;

    public async Task RequestReply(CancellationToken cancellationToken)
    {
        ChatAudioUI.Enable();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            return; // Already hot - idempotent

        var armed = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var focused = ChatUI.SelectedChatId.Value;
        var snapshot = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var target = ReplyTargetResolver.Resolve(
            armed, snapshot, focused, Clocks.ServerClock.Now, Constants.Audio.WalkieTalkieReplyRecencyWindow);
        if (target is not { } chatId) {
            _ = TuneUI.Play(Tune.WalkieReplyNothingHeard);
            return;
        }

        if (!await AudioRecorder.MicrophonePermission.CheckOrRequest(cancellationToken).ConfigureAwait(false))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        // Opening the mic lifts a soft "mute all" applied by the host, exactly like RecorderToggle.
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id is { } ownAuthorId)
            await LiveSessionUI.MutePeer(chatId, ownAuthorId, false, cancellationToken).ConfigureAwait(false);
        await ChatAudioUI.SetRecordingChatId(chatId, isPushToTalk: true).ConfigureAwait(false);

        StartColdStartWatch(chatId);
    }

    public async Task StopReply()
    {
        bool everVoiced;
        lock (_lock)
            everVoiced = _everVoiced;
        StopColdStartWatch();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is null)
            return; // Already closed - idempotent

        await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
        _ = TuneUI.Play(everVoiced ? Tune.WalkieReplyEnded : Tune.WalkieReplyNothingHeard);
    }

    // Private methods

    private void StartColdStartWatch(ChatId chatId)
    {
        lock (_lock) {
            _coldStartCts.CancelAndDisposeSilently();
            _everVoiced = false;
            var cts = new CancellationTokenSource();
            _coldStartCts = cts;
            _ = BackgroundTask.Run(
                () => ColdStartWatch(chatId, cts),
                Log, $"{nameof(ColdStartWatch)} failed",
                cts.Token);
        }
    }

    private void StopColdStartWatch()
    {
        lock (_lock) {
            _coldStartCts.CancelAndDisposeSilently();
            _coldStartCts = null;
        }
    }

    private async Task ColdStartWatch(ChatId chatId, CancellationTokenSource cts)
    {
        var cancellationToken = cts.Token;
        var coldTimeout = Constants.Audio.WalkieTalkieReplyColdStartTimeout;
        var startedAt = CpuTimestamp.Now;

        // Phase 1: cold-start dead-man - wait for the first IsVoiceActive on the target chat,
        // or close the mic with the "nothing heard" cue once coldTimeout elapses without voice.
        var cState = AudioRecorder.State.Computed;
        while (true) {
            cState = await cState.Update(cancellationToken).ConfigureAwait(false);
            var state = cState.Value;
            if (state.ChatId == chatId && state.IsVoiceActive)
                break;
            if (ShouldColdClose(false, startedAt.Elapsed, coldTimeout)) {
                await CloseFromWatcher(cts, everVoiced: false).ConfigureAwait(false);
                return;
            }

            var remaining = (coldTimeout - startedAt.Elapsed).Positive();
            using var stepCts = cancellationToken.CreateLinkedTokenSource();
            var whenChanged = cState.WhenInvalidated(stepCts.Token);
            var whenTimeout = Clocks.CpuClock.Delay(remaining, stepCts.Token);
            await Task.WhenAny(whenChanged, whenTimeout).ConfigureAwait(false);
            stepCts.CancelAndDisposeSilently();
        }
        lock (_lock)
            _everVoiced = true;

        // Phase 2: hot phase - the existing RecordChat idle owns the close (RecordingDuration,
        // incoming-from-others reset, manual stop). We only wait for recording to end to play the cue.
        var cRecordingChatId = await Computed
            .Capture(ChatAudioUI.GetRecordingChatId, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var (recordingChatId, _) in cRecordingChatId.Changes(cancellationToken).ConfigureAwait(false))
            if (recordingChatId != chatId) {
                await CloseFromWatcher(cts, everVoiced: true).ConfigureAwait(false);
                return;
            }
    }

    private async Task CloseFromWatcher(CancellationTokenSource cts, bool everVoiced)
    {
        lock (_lock) {
            if (ReferenceEquals(_coldStartCts, cts)) {
                cts.DisposeSilently();
                _coldStartCts = null;
            }
        }
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
        _ = TuneUI.Play(everVoiced ? Tune.WalkieReplyEnded : Tune.WalkieReplyNothingHeard);
    }
}
