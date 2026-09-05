using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

// Not a compute service: it has no compute methods, and a sealed class gets no generated
// proxy, so a fusion.AddService registration would fail on first resolution.

/// <summary>
/// Coordinates a PTT voice reply: resolves the target chat, opens the hot mic,
/// and runs a cold-start dead-man switch that closes the mic if no voice is heard in time.
/// Native triggers (iOS PTT, Android widget, gestures) drive it via <see cref="RequestReply"/>/<see cref="StopReply"/>.
/// </summary>
public sealed class PttReplyUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private readonly Lock _lock = new();
    private CancellationTokenSource? _coldStartCts;
    private PttReply? _reply;
    private bool _everVoiced;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private ChatUI ChatUI => Hub.ChatUI;
    private IChats Chats => Hub.Chats;
    private BackgroundStateTracker BackgroundStateTracker
        => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    public static bool ShouldColdClose(bool everVoiced, TimeSpan elapsed, TimeSpan coldTimeout)
        => !everVoiced && elapsed >= coldTimeout;

    public static bool ShouldReportMicFailure(bool hasSignal, TimeSpan elapsed, TimeSpan timeout)
        // Keyed on captured samples, not on IsRecording: the recorder reports itself started as
        // soon as AudioRecord initializes, which it does even when the OS then hands it nothing.
        => !hasSignal && elapsed >= timeout;

    public Task<PttReply?> RequestReply(CancellationToken cancellationToken)
        => RequestReply(null, cancellationToken);

    // A null recencyWindow means "the user's answer window"; it can't be resolved here because
    // the setting comes from the same settings read the method already awaits.
    public async Task<PttReply?> RequestReply(TimeSpan? recencyWindow, CancellationToken cancellationToken)
    {
        // Null unless this call itself opened the mic, so no caller can stop someone else's reply.
        ChatAudioUI.Enable();
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) is not null)
            return null; // Already hot - idempotent

        var settings = await UserSettingsUI.UserPttSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        var armed = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
        var focused = ChatUI.SelectedChatId.Value;
        var snapshot = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var target = ReplyTargetResolver.Resolve(
            armed, snapshot, focused, Clocks.ServerClock.Now,
            recencyWindow ?? settings.AnswerWindow, settings.AnswerWindow);
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

        var reply = new PttReply(chatId, Clocks.SystemClock.Now);
        // The hold precedes the publish: a competitor can only displace this reply after seeing it
        // in _reply, so its Release always follows this Hold and the count never dips.
        PttMicCapability.Hold(reply);
        PttReply? displacedReply;
        lock (_lock) {
            displacedReply = _reply;
            _reply = reply;
            // The displaced reply's watcher dies here, inside the lock: alive, it could still pass
            // its ownership gate and close the mic this call is about to open.
            _coldStartCts.CancelAndDisposeSilently();
            _coldStartCts = null;
        }
        if (displacedReply is not null)
            PttMicCapability.Release(displacedReply);

        var isBackground = BackgroundStateTracker.IsBackground.Value || ChatAudioUI.IsPttHeadless;
        try {
            await ChatAudioUI.SetRecordingChatId(chatId,
                    isPtt: true,
                    idleDuration: GetEffectiveHotWindow(settings.HotWindow, isBackground),
                    mustPlayBeginTune: settings.AreAudibleCuesEnabled)
                .ConfigureAwait(false);
        }
        catch {
            lock (_lock)
                if (_reply == reply)
                    _reply = null;
            PttMicCapability.Release(reply);
            throw;
        }

        StartColdStartWatch(chatId);
        _ = BackgroundTask.Run(
            () => WatchMicLive(reply),
            Log, $"{nameof(WatchMicLive)} failed",
            CancellationToken.None);
        return reply;
    }

    public async Task StopReply(PttReply reply)
    {
        // The identity check is what keeps a native trigger from closing a mic it didn't open:
        // anything that opens a reply in the meantime replaces _reply, and this then no-ops.
        lock (_lock) {
            if (_reply != reply)
                return;

            _reply = null;
        }
        if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) != reply.ChatId) {
            PttMicCapability.Release(reply);
            return;
        }

        await CloseReply(reply).ConfigureAwait(false);
    }

    public Task StopReply()
    {
        PttReply? ownReply;
        lock (_lock) {
            ownReply = _reply;
            _reply = null;
        }
        return CloseReply(ownReply);
    }

    public Task PlayFailureCue()
        // Ungated on purpose: the cue is vibration-only and it is the sole signal that a
        // start trigger silently did nothing - symmetric with the ungated gesture ack.
        => TuneUI.Play(Tune.PttReplyFailed);

    public static TimeSpan GetEffectiveHotWindow(TimeSpan hotWindow, bool isBackground)
        // A background activation has no visible mic indicator to prompt a manual stop,
        // so the hot window shrinks to keep an unnoticed open mic short.
        => isBackground
            ? TimeSpanExt.Min(hotWindow, Constants.Audio.PttReplyBackgroundHotWindow)
            : hotWindow;

    // Private methods

    private async Task CloseReply(PttReply? ownReply)
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
                // Closing any recording is intended, but a PTT cue would be a lie about a mic PTT
                // never opened - and _everVoiced is stale for it anyway.
                Log.LogDebug("CloseReply: closed a recording PTT didn't open");
                return;
            }

            _ = PlayCue(everVoiced ? Tune.PttReplyEnded : Tune.PttReplyNothingHeard);
        }
        finally {
            if (ownReply is not null)
                PttMicCapability.Release(ownReply);
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
        PttMicCapability.ReportBlocked();
        return false;
    }

    private async Task WatchMicLive(PttReply reply)
    {
        // SetRecordingChatId only writes the intent, and every way the capture below it can fail
        // - the mic withheld, a read error, a producer thread that ends - leaves the recorder
        // reporting success with nothing arriving. The begin tune has already played by then, so
        // without this the only feedback is the 15s "nothing heard" cue, which claims we listened.
        // The deadline runs from IsRecording, not from here: StartRecording legitimately takes
        // seconds, and timing it from the request killed replies that were about to work.
        var startBudget = Constants.Audio.PttReplyMicStartTimeout;
        if (!await WaitFor(reply, x => x.IsRecording, startBudget).ConfigureAwait(false)) {
            await ReportMicFailure(reply, "the recorder never started").ConfigureAwait(false);
            return;
        }

        var timeout = Constants.Audio.PttReplyMicLiveTimeout;
        if (!await WaitFor(reply, x => x.IsSignalDetected, timeout).ConfigureAwait(false))
            await ReportMicFailure(reply, "no captured audio").ConfigureAwait(false);
    }

    private async Task<bool> WaitFor(
        PttReply reply, Func<AudioRecorderState, bool> predicate, TimeSpan budget)
    {
        var startedAt = CpuTimestamp.Now;
        var cState = AudioRecorder.State.Computed;
        while (true) {
            var state = cState.Value;
            if (state.ChatId == reply.ChatId && predicate.Invoke(state))
                return true;
            if (ShouldReportMicFailure(false, startedAt.Elapsed, budget))
                return false;

            using var stepCts = new CancellationTokenSource();
            var whenChanged = cState.WhenInvalidated(stepCts.Token);
            var whenTimeout = Clocks.CpuClock.Delay((budget - startedAt.Elapsed).Positive(), stepCts.Token);
            await Task.WhenAny(whenChanged, whenTimeout).ConfigureAwait(false);
            stepCts.CancelAndDisposeSilently();
            cState = await cState.Update(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task ReportMicFailure(PttReply reply, string reason)
    {
        lock (_lock) {
            if (_reply != reply)
                return; // Superseded - whatever displaced it owns the mic now
        }

        Log.LogWarning(
            nameof(WatchMicLive) + ": chat #{ChatId} - {Reason}, closing the reply", reply.ChatId, reason);
        await StopReply(reply).ConfigureAwait(false);
        await PlayFailureCue().ConfigureAwait(false);
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
        var coldTimeout = Constants.Audio.PttReplyColdStartTimeout;
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
                await CloseFromWatcher(cts, chatId, everVoiced: false).ConfigureAwait(false);
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
                await CloseFromWatcher(cts, chatId, everVoiced: true).ConfigureAwait(false);
                return;
            }
    }

    private async Task CloseFromWatcher(CancellationTokenSource cts, ChatId chatId, bool everVoiced)
    {
        PttReply? ownReply;
        lock (_lock) {
            // A superseded watcher owns nothing anymore - closing here would hit whatever reply
            // displaced its own.
            if (!ReferenceEquals(_coldStartCts, cts))
                return;

            cts.DisposeSilently();
            _coldStartCts = null;
            ownReply = _reply;
            _reply = null;
        }
        try {
            if (await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false) == chatId)
                await ChatAudioUI.SetRecordingChatId(null).ConfigureAwait(false);
            await PlayCue(everVoiced ? Tune.PttReplyEnded : Tune.PttReplyNothingHeard).ConfigureAwait(false);
        }
        finally {
            if (ownReply is not null)
                PttMicCapability.Release(ownReply);
        }
    }

    private async Task PlayCue(Tune tune)
    {
        var areCuesEnabled = await UserSettingsUI.UserPttSettings()
            .Get(x => x.AreAudibleCuesEnabled, CancellationToken.None)
            .ConfigureAwait(false);
        if (areCuesEnabled)
            await TuneUI.Play(tune).ConfigureAwait(false);
    }
}

/// <summary>
/// Identifies one open PTT reply, so a trigger that opened a mic can tell it apart
/// from one another trigger opened in the same chat.
/// </summary>
public sealed record PttReply(ChatId ChatId, Moment StartedAt);
