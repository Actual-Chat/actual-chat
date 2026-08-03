using ActualChat.Users;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Owns the sensor subscription lifecycle and turns recognized gestures into
/// walkie-talkie reply start/stop. Sensors are live only while a reply is plausible.
/// </summary>
public sealed class GestureUI(AppUIHub hub) : UIWorkerBase<AppUIHub>(hub)
{
    private readonly GestureRecognizer _recognizer = new(
        new GestureOptions(false, false, false, ShakeSensitivity.Medium));
    private volatile bool _isPracticeMode;
    private int _sampleCount;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private WalkieTalkieReplyUI WalkieTalkieReplyUI => Hub.WalkieTalkieReplyUI;
    private UserSettingsUI UserSettingsUI => Hub.UserSettingsUI;

    public SensorFeed Feed { get; } = hub.Services.GetRequiredService<SensorFeed>();
    public float ShakePeakDeviation => _recognizer.ShakePeakDeviation;
    public int SampleCount => Volatile.Read(ref _sampleCount);
    public event Action<GestureEvent>? PracticeGestureDetected;

    public bool IsPracticeMode {
        get => _isPracticeMode;
        set {
            _isPracticeMode = value;
            _recognizer.Reset();
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        Feed.SampleReceived += OnSample;
        Feed.ProximityChanged += OnProximityChanged;
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(TrackActivation)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task TrackActivation(CancellationToken cancellationToken)
    {
        var isSensing = false;
        var isProximityOn = false;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var settings = await UserSettingsUI.UserWalkieTalkieSettings()
                    .Get(cancellationToken)
                    .ConfigureAwait(false);
                var isFaceDownStopEnabled = await UserSettingsUI.UserAppSettings()
                    .Get(x => x.IsFaceDownMicStopEnabled ?? false, cancellationToken)
                    .ConfigureAwait(false);
                var pttChatIds = await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false);
                var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
                var isMicOpen = recordingChatId is not null;
                var mustSenseStart = GestureActivationPolicy.ShouldSenseStartGestures(
                    settings.AreGesturesAlwaysOn,
                    _isPracticeMode,
                    pttChatIds,
                    IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt(),
                    Clocks.ServerClock.Now,
                    Constants.Audio.WalkieTalkieReplyRecencyWindow);
                var mustSenseStop = isFaceDownStopEnabled && (isMicOpen || _isPracticeMode);

                _recognizer.Options = new GestureOptions(
                    settings.IsFlipToTalkEnabled && mustSenseStart,
                    settings.IsDoubleShakeEnabled && mustSenseStart,
                    mustSenseStop,
                    settings.ShakeSensitivity);

                var mustSense = mustSenseStart || mustSenseStop;
                if (mustSense != isSensing) {
                    isSensing = mustSense;
                    if (mustSense)
                        Feed.StartAccelerometer();
                    else {
                        Feed.StopAccelerometer();
                        _recognizer.Reset();
                    }
                }
                if (mustSenseStop != isProximityOn) {
                    isProximityOn = mustSenseStop;
                    if (mustSenseStop)
                        Feed.StartProximity();
                    else
                        Feed.StopProximity();
                }

                await Clocks.CpuClock.Delay(Constants.Audio.WalkieTalkieIdleCheckPeriod, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally {
            Feed.StopAccelerometer();
            Feed.StopProximity();
        }
    }

    private void OnProximityChanged(bool isCovered)
        => _recognizer.SetProximityCovered(isCovered);

    private void OnSample(SensorSample sample)
    {
        Interlocked.Increment(ref _sampleCount);
        if (_recognizer.Process(sample) is not { } gesture)
            return;

        // Practice never transmits: rehearsing a gesture in Settings must not open the mic.
        if (_isPracticeMode) {
            PracticeGestureDetected?.Invoke(gesture);
            return;
        }

        var whenHandled = gesture.Kind == GestureKind.FaceDown
            ? ChatAudioUI.SetRecordingChatId(null).AsTask()
            : WalkieTalkieReplyUI.RequestReply(CancellationToken.None);
        _ = BackgroundTask.Run(() => whenHandled, Log, $"{gesture.Kind} handling failed", CancellationToken.None);
    }
}
