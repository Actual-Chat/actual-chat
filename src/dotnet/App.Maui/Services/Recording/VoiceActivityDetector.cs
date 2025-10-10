using ActualChat.Hosting;
using MathExt = ActualChat.Mathematics.MathExt;

namespace ActualChat.App.Maui.Services.Recording;

/// <summary>
///     Neural voice activity detector backed by the same ONNX model and logic used in the TypeScript implementation.
///     - Sample rate: 16 kHz
///     - Window: 32 ms (512 samples)
///     - Context: 64 samples prepended to each window (total model input = 576 floats)
///     - Recurrent state: [2,1,128] float tensor persisted between calls
/// </summary>
public abstract class VoiceActivityDetector(IServiceProvider services) : IAsyncDisposable, IDisposable
{
    // AUDIO_REC constants (subset)
    public const int SampleRate = Constants.Audio.RecordingSampleRate; // AUDIO_REC.SAMPLE_RATE
    protected const int WindowSamples = Constants.Audio.VadFrameLength; // 512
    protected const double MinRecordingGain = 0.0005; // AR.MIN_RECORDING_GAIN

    // AUDIO_VAD constants (subset)
    protected const int ContextSamples = 64; // AUDIO_VAD.NN_VAD_CONTEXT_SAMPLES
    protected const int InputSamples = WindowSamples + ContextSamples; // 576
    private const double MinSpeechS = 0.5;
    private const double MaxSpeechS = 60 * 2;
    private const double MinSpeechToCancelPauseS = 0.15;
    private const double MinPauseS = 0.2;
    private const double MaxPauseS = 2.7;
    private const double MaxConvPauseS = 0.65;
    private const double ConvDurationS = 30;
    private const double PauseVariesFromS = 10;
    private static readonly double PauseVaryPower = Math.Sqrt(2);
    private readonly RunningEma _longProbEma = new (0.5f, 64);
    private readonly RunningEma _gainEma = new (0, 10);

    // Processing state (mirrors TS VoiceActivityDetectorBase)
    private readonly RunningEma _probEma = new (0.5f, 5);
    private readonly RunningUnitMedian _probMedian = new ();

    private bool _disposed;
    private long? _lastConversationSignalAtSample;
    private int _maxPauseSamples;
    private int _pauseCancelSamples;
    private long? _pauseOffset;

    private long _sampleCount;

    private RunningUnitMedian? _whenTalkingProbMedian;
    protected readonly HostInfo HostInfo = services.HostInfo();
    protected readonly ILogger Log = services.LogFor<VoiceActivityDetector>();
    public VoiceActivityChange LastActivityEvent { get; protected set; } = VoiceActivityChange.NoVoiceActivity;

    public abstract bool IsInitialized { get; }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }

    /// <summary>
    ///     Initializes the VAD by loading the NN model from the app package.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public abstract Task EnsureInitialized(CancellationToken cancellationToken = default);


    /// <summary>
    ///     Resets recurrent state and context to the initial values.
    /// </summary>
    public virtual void Reset()
    {
        ThrowIfDisposed();
        ResetProcessingState();
    }

    /// <summary>
    ///     Feeds a 32ms window, runs NN and applies VAD processing logic with EMA and median.
    ///     Returns either a VoiceActivityChange when state flips, or the current input gain when no change.
    ///     Mirrors the logic of VoiceActivityDetectorBase.appendChunk in TS.
    /// </summary>
    public virtual VadResult AppendChunk(ReadOnlySpan<float> monoPcm)
    {
        var currentOffset = _sampleCount;
        _sampleCount += monoPcm.Length;
        var currentEvent = LastActivityEvent;

        var rawGain = AudioExt.ApproximateGain(monoPcm);
        _gainEma.AppendSample(rawGain);
        var gain = _gainEma.Value;
        if (gain < MinRecordingGain && currentEvent.Kind == VoiceActivityKind.End)
            return VadResult.GainOnly(gain);

        var prob = AppendChunkInternal(monoPcm);
        if (prob is null)
            return VadResult.GainOnly(gain);

        //Log.LogInformation("VAD: {Offset} {Kind} {Prob} {Gain}", currentOffset, currentEvent.Kind, prob.Value, gain);

        _probEma.AppendSample(prob.Value);
        _longProbEma.AppendSample(prob.Value);
        var probEma = _probEma.Value;
        var longProbEma = _longProbEma.Value;
        var probMedian = _probMedian.Value;
        var speechProbTrigger = 0.67 * probMedian;
        var pauseProbTrigger = 0.15 * probMedian;

        if (currentEvent.Kind == VoiceActivityKind.End && probEma >= longProbEma && probEma >= speechProbTrigger) {
            // speech start detected
            var offset = (int)Math.Max(0, currentOffset - monoPcm.Length);
            var duration = offset - currentEvent.OffsetSamples;
            currentEvent = VoiceActivityChange.Start(offset, duration, probEma);
            _whenTalkingProbMedian = new RunningUnitMedian();
            _maxPauseSamples = (int)(SampleRate * MaxPauseS);
        }
        else if (currentEvent.Kind == VoiceActivityKind.Start && probEma < longProbEma && probEma < pauseProbTrigger) {
            // pause start detected
            _pauseCancelSamples = 0;
            _pauseOffset ??= currentOffset;
            _whenTalkingProbMedian = null;
        }

        if (currentEvent.Kind == VoiceActivityKind.Start) {
            var currentSpeechSamples = (int)(currentOffset - currentEvent.OffsetSamples);

            if (_pauseOffset is not null) {
                // we detected pause earlier - should we materialize it?
                if (probEma >= speechProbTrigger) {
                    // and it's speech now
                    _pauseCancelSamples += monoPcm.Length;
                    if (_pauseCancelSamples >= (int)(SampleRate * MinSpeechToCancelPauseS)) {
                        _pauseOffset = null;
                        _pauseCancelSamples = 0;
                    }
                }
                else if (probEma < pauseProbTrigger) {
                    // it's still a pause
                    var currentSilenceSamples = (int)(currentOffset - (_pauseOffset ?? 0));
                    if (currentSilenceSamples > _maxPauseSamples
                        && currentSpeechSamples > (int)(SampleRate * MinSpeechS)) {
                        // materializing the pause
                        var offset = (int)(_pauseOffset! + monoPcm.Length);
                        var duration = offset - currentEvent.OffsetSamples;
                        currentEvent = VoiceActivityChange.End(offset, duration, probEma);
                        _pauseOffset = null;
                    }
                }
            }
            else if (_whenTalkingProbMedian is not null) {
                // adjust speech boundary triggers if current period was speech with high probabilities
                var offset = currentOffset + monoPcm.Length;
                var duration = (int)(offset - currentEvent.OffsetSamples);
                var durationS = duration / (double)SampleRate;
                var speechRatio = _whenTalkingProbMedian.SampleCount / (double)duration;
                if (speechRatio > 0.5 && durationS > 2)
                    _probMedian.AppendSample((float)probEma);
            }

            if (currentEvent.Kind == VoiceActivityKind.Start && currentSpeechSamples > (int)(SampleRate * MaxSpeechS)) {
                // break long speech regardless of speech probability
                var offset = (int)(_pauseOffset ?? currentOffset);
                var duration = offset - currentEvent.OffsetSamples;
                currentEvent = VoiceActivityChange.End((int)currentOffset, duration, probEma);
                _pauseOffset = null;
            }

            if (_whenTalkingProbMedian is not null && prob > 0.25)
                _whenTalkingProbMedian.AppendSample(prob.Value);

            // adjust max pause for long speech - break more aggressively, but keep longer pauses for monologue
            var isConversation = _lastConversationSignalAtSample is not null
                && (_sampleCount - _lastConversationSignalAtSample) <= (long)(SampleRate * ConvDurationS);
            var maxPause = isConversation ? MaxConvPauseS : MaxPauseS;
            var maxPauseVariesFromSamples = (int)(SampleRate * PauseVariesFromS);
            var maxPauseAlpha = (currentSpeechSamples - maxPauseVariesFromSamples)
                / (double)((int)(SampleRate * MaxSpeechS) - maxPauseVariesFromSamples);
            maxPauseAlpha = MathExt.Clamp(maxPauseAlpha, 0, 1);
            maxPauseAlpha = Math.Pow(maxPauseAlpha, PauseVaryPower);
            var silenceThreshold = MathExt.Lerp(maxPause, MinPauseS, maxPauseAlpha);
            _maxPauseSamples = (int)Math.Floor(SampleRate * silenceThreshold);
        }

        if (LastActivityEvent.Equals(currentEvent) || LastActivityEvent.Kind == currentEvent.Kind)
            return VadResult.GainOnly(gain);

        LastActivityEvent = currentEvent;
        return VadResult.Event(currentEvent);
    }

    /// <summary>
    /// Indates that an ongoing conversation is happening, which makes the VAD more aggressive in breaking pauses.
    /// Called when new message from another user arrives.
    /// </summary>
    public virtual void ConversationSignal()
        => _lastConversationSignalAtSample = _sampleCount;

    /// <summary>
    ///     Appends a single 32ms mono PCM window (Float32, 16 kHz) and returns speech probability [0..1].
    ///     Returns null if EnsureInitialized has not completed yet.
    /// </summary>
    /// <param name="monoPcm">512-sample window of mono PCM at 16 kHz</param>
    protected abstract float? AppendChunkInternal(ReadOnlySpan<float> monoPcm);

    protected void ResetProcessingState()
    {
        _probEma.Reset();
        _longProbEma.Reset();
        _probMedian.Reset();
        _gainEma.Reset();
        _whenTalkingProbMedian = null;
        _sampleCount = 0;
        _pauseOffset = null;
        _pauseCancelSamples = 0;
        _lastConversationSignalAtSample = null;
        _maxPauseSamples = (int)(SampleRate * MaxPauseS);
        LastActivityEvent = VoiceActivityChange.NoVoiceActivity;
    }

    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VoiceActivityDetector));
    }
}
