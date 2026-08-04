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
    private WalkieTalkieReply? _reply;
    private bool _everVoiced;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;

    public static bool ShouldColdClose(bool everVoiced, TimeSpan elapsed, TimeSpan coldTimeout)
        => !everVoiced && elapsed >= coldTimeout;

    public Task<WalkieTalkieReply?> RequestReply(CancellationToken cancellationToken)
        => RequestReply(Constants.Audio.WalkieTalkieReplyRecencyWindow, cancellationToken);

    public async Task<WalkieTalkieReply?> RequestReply(TimeSpan recencyWindow, CancellationToken cancellationToken)
    {
        // Null unless this call itself opened the mic, so no caller can stop someone else's reply.
        ChatAudioUI.Enable();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            return null; // Already hot - idempotent

        var settings = await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var armed = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var focused = ChatUI.SelectedChatId.Value;
        var snapshot = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var target = ReplyTargetResolver.Resolve(
            armed, snapshot, focused, Clocks.ServerClock.Now, recencyWindow);
        if (target is not { } chatId) {
            await PlayFailureCue().ConfigureAwait(false);
            return null;
        }
        if (!await HasMicrophonePermission(cancellationToken).ConfigureAwait(false)) {
            await PlayFailureCue().ConfigureAwait(false);
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Opening the mic lifts a soft "mute all" applied by the host, exactly like RecorderToggle.
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id is { } ownAuthorId)
            await LiveSessionUI.MutePeer(chatId, ownAuthorId, false, cancellationToken).ConfigureAwait(false);

        // Published before the mic opens: WalkieReplyToggle recomputes on the recording-chat change
        // and must already see this reply as walkie-owned.
        var reply = new WalkieTalkieReply(chatId, Clocks.SystemClock.Now);
        WalkieTalkieReply? displacedReply;
        lock (_lock) {
            displacedReply = _reply;
            _reply = reply;
        }
        // The new hold first, so the capability can't dip between the two.
        WalkieTalkieMicCapability.Hold(reply);
        if (displacedReply is not null)
            WalkieTalkieMicCapability.Release(displacedReply);

        try {
            await ChatAudioUI.SetRecordingChatId(chatId,
                    isPushToTalk: true,
                    idleDuration: settings.HotWindow,
                    mustPlayBeginTune: settings.AreAudibleCuesEnabled)
                .ConfigureAwait(false);
        }
        catch {
            lock (_lock)
                if (_reply == reply)
                    _reply = null;
            WalkieTalkieMicCapability.Release(reply);
            throw;
        }

        StartColdStartWatch(chatId);
        return reply;
    }

    public async Task StopReply(WalkieTalkieReply reply)
    {
        // The identity check is what keeps a native trigger from closing a mic it didn't open:
        // anything that opens a reply in the meantime replaces _reply, and this then no-ops.
        lock (_lock) {
            if (_reply != reply)
                return;

            _reply = null;
        }
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) != reply.ChatId) {
            WalkieTalkieMicCapability.Release(reply);
            return;
        }

        await CloseReply(reply).ConfigureAwait(false);
    }

    public Task StopReply()
    {
        WalkieTalkieReply? ownReply;
        lock (_lock) {
            ownReply = _reply;
            _reply = null;
        }
        return CloseReply(ownReply);
    }

    public bool IsOwnReply(ChatId chatId)
    {
        lock (_lock)
            return _reply is { } reply && reply.ChatId == chatId;
    }

    public Task PlayFailureCue()
        => PlayCue(Tune.WalkieReplyNothingHeard);

    // Private methods

    private async Task CloseReply(WalkieTalkieReply? ownReply)
    {
        bool everVoiced;
        lock (_lock)
            everVoiced = _everVoiced;

        StopColdStartWatch();
        try {
            if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is null)
                return; // Already closed - idempotent

            await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
            if (ownReply is null) {
                // Closing any recording is intended, but a walkie cue would be a lie about a mic walkie
                // never opened - and _everVoiced is stale for it anyway.
                Log.LogDebug("CloseReply: closed a recording walkie-talkie didn't open");
                return;
            }

            _ = PlayCue(everVoiced ? Tune.WalkieReplyEnded : Tune.WalkieReplyNothingHeard);
        }
        finally {
            if (ownReply is not null)
                WalkieTalkieMicCapability.Release(ownReply);
        }
    }

    private async Task<bool> HasMicrophonePermission(CancellationToken cancellationToken)
    {
        var permission = AudioRecorder.MicrophonePermission;
        if (await permission.Check(cancellationToken).ConfigureAwait(false) == true)
            return true;
        if (permission.CanPrompt)
            return await permission.CheckOrRequest(cancellationToken).ConfigureAwait(false);

        Log.LogWarning("RequestReply: no microphone permission, and nothing can request it headlessly");
        return false;
    }

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
        WalkieTalkieReply? ownReply = null;
        lock (_lock) {
            if (ReferenceEquals(_coldStartCts, cts)) {
                cts.DisposeSilently();
                _coldStartCts = null;
                // This watch owned the open reply, so _reply must not outlive it: a stale one
                // would let a native trigger close whatever recording comes next.
                ownReply = _reply;
                _reply = null;
            }
        }
        try {
            if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
                await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
            await PlayCue(everVoiced ? Tune.WalkieReplyEnded : Tune.WalkieReplyNothingHeard).ConfigureAwait(false);
        }
        finally {
            if (ownReply is not null)
                WalkieTalkieMicCapability.Release(ownReply);
        }
    }

    private async Task PlayCue(Tune tune)
    {
        var areCuesEnabled = await UserSettingsUI.UserWalkieTalkieSettings()
            .Get(x => x.AreAudibleCuesEnabled, CancellationToken.None)
            .ConfigureAwait(false);
        if (areCuesEnabled)
            await TuneUI.Play(tune).ConfigureAwait(false);
    }
}

/// <summary>
/// Identifies one open walkie-talkie reply, so a trigger that opened a mic can tell it apart
/// from one another trigger opened in the same chat.
/// </summary>
public sealed record WalkieTalkieReply(ChatId ChatId, Moment StartedAt);
