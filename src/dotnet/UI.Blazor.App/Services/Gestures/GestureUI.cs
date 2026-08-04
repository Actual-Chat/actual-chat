using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Users;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Owns the sensor subscription lifecycle and turns recognized gestures into
/// walkie-talkie reply start/stop. Sensors are live only while a reply is plausible.
/// </summary>
public sealed class GestureUI : UIWorkerBase<AppUIHub>
{
    private static readonly GestureOptions DisarmedOptions = new(false, false, false, ShakeSensitivity.Medium);

    private readonly GestureRecognizer _recognizer = new(DisarmedOptions);
    private volatile bool _isPracticeMode;
    private bool _isHeadsetButtonEnabled;
    private bool _hasAnswerWindow;
    private int _sampleCount;
    private TaskCompletionSource _wakeSignal = TaskCompletionSourceExt.New();

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private WalkieTalkieReplyUI WalkieTalkieReplyUI => Hub.WalkieTalkieReplyUI;

    public SensorFeed Feed { get; }
    public SyncedState<UserAppSettings> AppSettings { get; }
    public GestureOptions RecognizerOptions => _recognizer.Options;
    public float ShakePeakDeviation => _recognizer.ShakePeakDeviation;
    public int SampleCount => Volatile.Read(ref _sampleCount);
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
            // Computed<StoredSettings>. GetPttChatIds reads the whole UserWalkieTalkieSettings
            // record, so its invalidation also covers the flip/shake/sensitivity toggles below.
            var cPttChatIds = await Computed
                .Capture(() => ChatAudioUI.GetPttChatIds(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            var cRecordingChatId = await Computed
                .Capture(ChatAudioUI.GetRecordingChatId, cancellationToken)
                .ConfigureAwait(false);

            var minPeriod = Constants.Audio.WalkieTalkieGestureCheckMinPeriod;
            var lastCheckAt = Clocks.CpuClock.Now - minPeriod;
            while (!cancellationToken.IsCancellationRequested) {
                var minWait = minPeriod - (Clocks.CpuClock.Now - lastCheckAt);
                if (minWait > TimeSpan.Zero)
                    await Clocks.CpuClock.Delay(minWait, cancellationToken).ConfigureAwait(false);

                lastCheckAt = Clocks.CpuClock.Now;
                cPttChatIds = await cPttChatIds.Update(cancellationToken).ConfigureAwait(false);
                cRecordingChatId = await cRecordingChatId.Update(cancellationToken).ConfigureAwait(false);

                // Every wake source is subscribed before the state it guards is read: a change
                // landing in between must complete a signal this iteration still awaits.
                using var waitCts = cancellationToken.CreateLinkedTokenSource();
                var whenWoken = Volatile.Read(ref _wakeSignal).Task;
                var cAppSettings = AppSettings.Computed;
                var whenPttChatIdsChanged = cPttChatIds.WhenInvalidated(waitCts.Token);
                var whenRecordingChanged = cRecordingChatId.WhenInvalidated(waitCts.Token);
                var whenAppSettingsChanged = cAppSettings.WhenInvalidated(waitCts.Token);

                var isPracticeMode = _isPracticeMode;
                var pttChatIds = cPttChatIds.Value;
                var isMicOpen = cRecordingChatId.Value is not null;
                var isFaceDownStopEnabled = cAppSettings.Value.IsFaceDownMicStopEnabled ?? false;
                var settings = await UserSettingsUI.UserWalkieTalkieSettings()
                    .Get(cancellationToken)
                    .ConfigureAwait(false);
                var lastIncomingVoiceAt = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
                var now = Clocks.ServerClock.Now;
                var recencyWindow = Constants.Audio.WalkieTalkieReplyRecencyWindow;
                var mustSenseStart = GestureActivationPolicy.ShouldSenseStartGestures(
                    settings.AreGesturesAlwaysOn,
                    isPracticeMode,
                    pttChatIds,
                    lastIncomingVoiceAt,
                    now,
                    recencyWindow);
                var mustSenseStop = isFaceDownStopEnabled && (isMicOpen || isPracticeMode);
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
                if (mustSenseStop)
                    Feed.StartProximity();
                else
                    Feed.StopProximity();

                // WalkieTalkieIdleCheckPeriod is the wall-clock floor for the answer-window expiry;
                // everything else races it so a state change takes effect on the next tick.
                var whenTimeout = Clocks.CpuClock.Delay(Constants.Audio.WalkieTalkieIdleCheckPeriod, waitCts.Token);
                await Task.WhenAny(
                        whenTimeout,
                        whenWoken,
                        whenPttChatIdsChanged,
                        whenRecordingChanged,
                        whenAppSettingsChanged)
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
        => _recognizer.SetProximityCovered(isCovered);

    private void OnIncomingVoiceStamped()
        => Wake();

    private void OnSample(SensorSample sample)
    {
        Interlocked.Increment(ref _sampleCount);
        var isPracticeMode = _isPracticeMode;
        if (_recognizer.Process(sample) is not { } gesture)
            return;

        var route = GestureActivationPolicy.Route(gesture.Kind, isPracticeMode);
        if (route == GestureRoute.None)
            return;
        if (route == GestureRoute.Practice) {
            PracticeGestureDetected?.Invoke(gesture);
            return;
        }

        // The hold is taken synchronously here: on Android it re-types the running foreground
        // service as a microphone one, and a headless session has no other path that would. It is
        // released when the trigger ends, so a reply that never opened can't leave it raised.
        var whenHandled = route == GestureRoute.StartReply
            ? WalkieTalkieMicCapability.HoldWhile(() => WalkieTalkieReplyUI.RequestReply(CancellationToken.None))
            : WalkieTalkieReplyUI.StopReply();
        _ = BackgroundTask.Run(() => whenHandled, Log, $"{gesture.Kind} handling failed", CancellationToken.None);
    }

    private void Wake()
    {
        var signal = Volatile.Read(ref _wakeSignal);
        signal.TrySetResult();
        // CompareExchange, so a racing Wake can't replace a signal the loop is already awaiting.
        Interlocked.CompareExchange(ref _wakeSignal, TaskCompletionSourceExt.New(), signal);
    }
}
