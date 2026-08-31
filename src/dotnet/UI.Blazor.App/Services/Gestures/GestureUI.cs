using System.Text;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Owns the sensor subscription lifecycle and turns recognized gestures into
/// PTT reply start/stop. Sensors are live only while a reply is plausible.
/// </summary>
public sealed class GestureUI : UIWorkerBase<AppUIHub>
{
    private static readonly GestureOptions DisarmedOptions = new(false, false, false, ShakeSensitivity.Medium);

    private readonly GestureRecognizer _recognizer = new(DisarmedOptions);
    // Ring of the last ~5s of samples (at Constants.Audio.GestureSampleMinPeriod), logged on
    // FaceDown fire - the only forensic record of what led to a gesture once it's fired on-device.
    private readonly SensorSample[] _recentSamples = new SensorSample[100];
    private int _recentSampleIndex;
    private string _lastGuardStatus = "off";
    private volatile bool _isPracticeMode;
    private bool _isHeadsetButtonEnabled;
    private bool _hasAnswerWindow;
    private int _sampleCount;
    private Moment? _lastForegroundedAt;
    private TaskCompletionSource _wakeSignal = TaskCompletionSourceExt.New();

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private ChatVideoUI ChatVideoUI => Hub.ChatVideoUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private PttReplyUI PttReplyUI => Hub.PttReplyUI;
    private BackgroundStateTracker BackgroundStateTracker => field ??= Services.GetRequiredService<BackgroundStateTracker>();

    public SensorFeed Feed { get; }
    public SyncedState<UserAppSettings> AppSettings { get; }
    public GestureOptions RecognizerOptions => _recognizer.Options;
    public float ShakePeakDeviation => _recognizer.ShakePeakDeviation;
    public string FaceDownStatus => _recognizer.FaceDownStatus;
    public string? FaceDownLastFireInfo => _recognizer.FaceDownLastFireInfo;
    public string GuardStatus => _recognizer.GuardStatus;
    public int SampleCount => Volatile.Read(ref _sampleCount);
    public SensorSample LastSample
        => _recentSamples[(_recentSampleIndex + _recentSamples.Length - 1) % _recentSamples.Length];
    public event Action<GestureEvent>? PracticeGestureDetected;

    public bool IsPracticeMode {
        get => _isPracticeMode;
        set {
            _isPracticeMode = value;
            _recognizer.Reset();
            if (!value) {
                // Disarm synchronously rather than waiting for the poll loop's next tick - leaving
                // flip/shake armed after the practice panel closes would let an ordinary jostle
                // call RequestReply for real.
                _recognizer.Options = DisarmedOptions;
                Feed.StopAccelerometer();
            }
            Wake();
        }
    }

    public GestureUI(AppUIHub hub) : base(hub)
    {
        Feed = hub.Services.GetRequiredService<SensorFeed>();
        AppSettings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            nameof(UserAppSettings),
            new UserAppSettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(AppSettings)));
        hub.RegisterDisposable(AppSettings);
    }

    public HeadsetButtonState GetHeadsetButtonState()
    {
        // The reads are fenced because TrackActivation publishes them from its own thread and
        // the native media-button handler calls this synchronously, off any of ours.
        var isEnabled = Volatile.Read(ref _isHeadsetButtonEnabled);
        var hasAnswerWindow = Volatile.Read(ref _hasAnswerWindow);
        return new(isEnabled, hasAnswerWindow, ChatAudioUI.IsRecording(), _isPracticeMode);
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // The loop has two consumers: the sensor callbacks below, and GetHeadsetButtonState, which
        // the native media-button handler reads - so a MAUI host must run it even with no working
        // accelerometer. Web has neither consumer, and would otherwise run this loop per circuit.
        var mustRun = Feed.IsAccelerometerAvailable || HostInfo.HostKind.IsMauiApp();
        if (!mustRun)
            return Task.CompletedTask;

        Feed.SampleReceived += OnSample;
        Feed.ProximityChanged += OnProximityChanged;
        IncomingVoiceActivityUI.IncomingVoiceStamped += OnIncomingVoiceStamped;
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(TrackActivation)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task TrackActivation(CancellationToken cancellationToken)
    {
        try {
            // Only compute methods may be captured: Computed.Capture<T> hard-casts the last node
            // used by the producer, and UserSettingsUI.Get is a plain method whose last node is a
            // Computed<StoredSettings>. GetPttChatIds reads the whole UserPttSettings
            // record, so its invalidation also covers the flip/shake/sensitivity toggles below.
            var cPttChatIds = await Computed
                .Capture(() => ChatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            var cRecordingChatId = await Computed
                .Capture(ChatAudioUI.GetRecordingChatId, cancellationToken)
                .ConfigureAwait(false);
            var cIsAnyOwnStreaming = await Computed
                .Capture(() => ChatVideoUI.IsAnyOwnStreaming(cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            var wasBackground = BackgroundStateTracker.IsBackground.Value;
            // Null while backgrounded/headless: a scope that starts without a visible app (PTT
            // wake, FGS) must not get the after-open arming window.
            _lastForegroundedAt = wasBackground ? null : Clocks.CpuClock.Now;
            var minPeriod = Constants.Audio.PttGestureCheckMinPeriod;
            var lastCheckAt = Clocks.CpuClock.Now - minPeriod;
            while (!cancellationToken.IsCancellationRequested) {
                var minWait = minPeriod - (Clocks.CpuClock.Now - lastCheckAt);
                if (minWait > TimeSpan.Zero)
                    await Clocks.CpuClock.Delay(minWait, cancellationToken).ConfigureAwait(false);

                lastCheckAt = Clocks.CpuClock.Now;
                cPttChatIds = await cPttChatIds.Update(cancellationToken).ConfigureAwait(false);
                cRecordingChatId = await cRecordingChatId.Update(cancellationToken).ConfigureAwait(false);
                cIsAnyOwnStreaming = await cIsAnyOwnStreaming.Update(cancellationToken).ConfigureAwait(false);

                // Every wake source is subscribed before the state it guards is read: a change
                // landing in between must complete a signal this iteration still awaits.
                using var waitCts = cancellationToken.CreateLinkedTokenSource();
                var whenWoken = Volatile.Read(ref _wakeSignal).Task;
                var cAppSettings = AppSettings.Computed;
                var cIsBackground = BackgroundStateTracker.IsBackground.Computed;
                var whenPttChatIdsChanged = cPttChatIds.WhenInvalidated(waitCts.Token);
                var whenRecordingChanged = cRecordingChatId.WhenInvalidated(waitCts.Token);
                var whenStreamingChanged = cIsAnyOwnStreaming.WhenInvalidated(waitCts.Token);
                var whenAppSettingsChanged = cAppSettings.WhenInvalidated(waitCts.Token);
                var whenBackgroundChanged = cIsBackground.WhenInvalidated(waitCts.Token);

                var isPracticeMode = _isPracticeMode;
                var isBackground = BackgroundStateTracker.IsBackground.Value;
                if (wasBackground && !isBackground)
                    _lastForegroundedAt = Clocks.CpuClock.Now;
                wasBackground = isBackground;
                var pttChatIds = cPttChatIds.Value;
                var isMicOpen = cRecordingChatId.Value is not null;
                var isFaceDownStopEnabled = !(cAppSettings.Value.IsFaceDownMicStopDisabled ?? false);
                var settings = await UserSettingsUI.UserPttSettings()
                    .Get(cancellationToken)
                    .ConfigureAwait(false);
                var lastIncomingVoiceAt = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
                var now = Clocks.ServerClock.Now;
                var recencyWindow = settings.AnswerWindow;
                var sinceForegrounded = _lastForegroundedAt is { } foregroundedAt
                    ? Clocks.CpuClock.Now - foregroundedAt
                    : TimeSpan.MaxValue;
                var mustSenseStart = GestureActivationPolicy.ShouldSenseStartGestures(
                    settings.AreGesturesAlwaysOn, isPracticeMode, sinceForegrounded,
                    pttChatIds, lastIncomingVoiceAt, now, recencyWindow);
                var isTransmitting = isMicOpen || cIsAnyOwnStreaming.Value;
                var mustSenseStop = GestureActivationPolicy.ShouldSenseStopGesture(
                    isFaceDownStopEnabled, isTransmitting, isPracticeMode);
                var buttonState = HeadsetButtonPolicy.GetState(
                    settings, pttChatIds, lastIncomingVoiceAt, now, recencyWindow, isMicOpen, isPracticeMode);
                Volatile.Write(ref _isHeadsetButtonEnabled, buttonState.IsEnabled);
                Volatile.Write(ref _hasAnswerWindow, buttonState.HasAnswerWindow);

                _recognizer.Options = new GestureOptions(
                    settings.IsFlipToTalkEnabled && mustSenseStart,
                    settings.IsDoubleShakeEnabled && mustSenseStart,
                    mustSenseStop,
                    settings.ShakeSensitivity);
                if (_isPracticeMode != isPracticeMode)
                    continue; // The setter raced this write and owns the disarm - re-decide

                // SensorFeed's Start/Stop are idempotent, so the loop states them every iteration
                // instead of tracking transitions the IsPracticeMode setter can silently undo.
                if (mustSenseStart || mustSenseStop)
                    Feed.StartAccelerometer();
                else {
                    Feed.StopAccelerometer();
                    _recognizer.Reset();
                }
                // Android only: iOS proximity monitoring blanks the screen while covered, which
                // is unacceptable with always-on arming - iOS keeps the orientation guard only.
                if (mustSenseStop || (mustSenseStart && OperatingSystem.IsAndroid()))
                    Feed.StartProximity();
                else
                    Feed.StopProximity();

                // PttIdleCheckPeriod is the wall-clock floor for the answer-window expiry;
                // everything else races it so a state change takes effect on the next tick.
                var whenTimeout = Clocks.CpuClock.Delay(Constants.Audio.PttIdleCheckPeriod, waitCts.Token);
                await Task.WhenAny(
                        whenTimeout,
                        whenWoken,
                        whenPttChatIdsChanged,
                        whenRecordingChanged,
                        whenStreamingChanged,
                        whenAppSettingsChanged,
                        whenBackgroundChanged)
                    .ConfigureAwait(false);
                waitCts.CancelAndDisposeSilently();
            }
        }
        finally {
            Feed.StopAccelerometer();
            Feed.StopProximity();
        }
    }

    private void OnProximityChanged(bool isCovered)
    {
        // Logged so gesture misbehavior can be traced back to a proximity transition on-device.
        Log.LogInformation("Proximity changed: covered={IsCovered}", isCovered);
        _recognizer.SetProximityCovered(isCovered);
    }

    private void OnIncomingVoiceStamped()
        => Wake();

    private void OnSample(SensorSample sample)
    {
        Interlocked.Increment(ref _sampleCount);
        _recentSamples[_recentSampleIndex] = sample;
        _recentSampleIndex = (_recentSampleIndex + 1) % _recentSamples.Length;
        // Logged so gesture misbehavior can be traced back to a guard-state transition on-device.
        var guardStatus = _recognizer.GuardStatus;
        if (guardStatus != _lastGuardStatus) {
            Log.LogInformation("Guard: {Old} -> {New} at {Sample}", _lastGuardStatus, guardStatus,
                $"{sample.X:F2},{sample.Y:F2},{sample.Z:F2}");
            _lastGuardStatus = guardStatus;
        }
        var isPracticeMode = _isPracticeMode;
        if (_recognizer.Process(sample) is not { } gesture)
            return;

        if (gesture.Kind == GestureKind.FaceDown)
            Log.LogWarning("FaceDown fired: {Info}; samples: {Samples}",
                _recognizer.FaceDownLastFireInfo, FormatRecentSamples());
        var route = GestureActivationPolicy.Route(gesture.Kind, isPracticeMode);
        if (route == GestureRoute.None)
            return;

        // Immediate tactile ack that the gesture registered - the mic-open cue comes later (or
        // never, in practice mode), which is too late to tell "not detected" from "not opened".
        _ = Hub.TuneUI.Play(Tune.PttGestureDetected);
        if (route == GestureRoute.Practice) {
            PracticeGestureDetected?.Invoke(gesture);
            return;
        }

        // The hold is taken synchronously here: on Android it re-types the running foreground
        // service as a microphone one, and a headless session has no other path that would. It is
        // released when the trigger ends, so a reply that never opened can't leave it raised.
        // Unbounded, like the widget and iOS PTT: a gesture explicitly asks to record, so it
        // resolves to the focused/last/single armed chat even with no recent incoming voice.
        var whenHandled = route == GestureRoute.StartReply
            ? PttMicCapability.HoldWhile(() => PttReplyUI.RequestReply(
                ReplyTargetResolver.UnboundedRecencyWindow, CancellationToken.None))
            : StopTransmitting();
        _ = BackgroundTask.Run(() => whenHandled, Log, $"{gesture.Kind} handling failed", CancellationToken.None);
    }

    private Task StopTransmitting()
    {
        // FaceDown means "I'm done transmitting": the mic reply and any outgoing camera or
        // screencast stream stop together.
        ChatVideoUI.StopStreaming();
        return PttReplyUI.StopReply();
    }

    private void Wake()
    {
        var signal = Volatile.Read(ref _wakeSignal);
        signal.TrySetResult();
        // CompareExchange, so a racing Wake can't replace a signal the loop is already awaiting.
        Interlocked.CompareExchange(ref _wakeSignal, TaskCompletionSourceExt.New(), signal);
    }

    private string FormatRecentSamples()
    {
        var samples = new List<SensorSample>(_recentSamples.Length);
        for (var i = 0; i < _recentSamples.Length; i++) {
            var sample = _recentSamples[(_recentSampleIndex + i) % _recentSamples.Length];
            if (sample.At != default)
                samples.Add(sample);
        }
        if (samples.Count == 0)
            return "";

        var t0 = samples[0].At;
        var sb = new StringBuilder(samples.Count * 24);
        foreach (var s in samples)
            sb.Append($"+{(s.At - t0).TotalMilliseconds:F0}:{s.X:F2},{s.Y:F2},{s.Z:F2};");

        return sb.ToString();
    }
}
