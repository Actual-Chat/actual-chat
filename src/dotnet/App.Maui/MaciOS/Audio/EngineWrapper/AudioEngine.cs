using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class AudioEngine : IDisposable
{
    public static readonly AVAudioFormat VoicePlaybackFormat =
        new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.PlaybackSampleRate, 1, false);
    public static readonly AVAudioFormat VoiceRecordingFormat =
        new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.RecordingSampleRate, 1, false);

    private readonly Lock _lock = new ();
    private readonly AVAudioEngine _engine = new ();
    private bool _wasStarted;
    private bool _isVoiceProcessingEnabled;
    private AVAudioMixerNode? _vpCaptureMixer;
    private AVAudioFormat? _vpCaptureFormat;
    private readonly ComputedState<bool> _isRunning;
    private AudioFocusMode Mode { get; }
    private AppUIHub Hub { get; }
    private ILogger Log => field ??= Hub.LogFor(GetType());
    public IState<bool> IsRunning => _isRunning;

    public bool IsRunningNow {
        get {
            lock (_lock)
                return _engine.Running;
        }
    }

    public AVAudioFormat? VoiceProcessedInputFormat {
        get {
            lock (_lock)
                return _vpCaptureFormat;
        }
    }

    public InputNode Input {
        get {
            lock (_lock)
                return field ??= new InputNode(_engine.InputNode, Hub);
        }
    }

    public AudioEngine(AudioFocusMode mode, AppUIHub hub)
    {
        Mode = mode;
        Hub = hub;
        _isRunning = hub.StateFactory.NewComputed(GetIsRunning, StateCategories.Get(GetType(), nameof(IsRunning)));
    }

    public void Dispose()
    {
        _isRunning.DisposeSilently();
        _engine.DisposeSilently();
    }

    public void EnsureRunning()
    {
        lock (_lock)
            EnsureEngineRunningUnsafe();
        _isRunning.Invalidate();
    }

    public void Pause()
    {
        if (!_wasStarted)
            return;

        lock (_lock) {
            if (!_wasStarted)
                return;

            _engine.Pause();
        }
        _isRunning.Invalidate();
    }

    public void Resume()
    {
        if (!_wasStarted)
            return;

        lock (_lock) {
            if (!_wasStarted)
                return;

            if (!TryEnsureEngineRunningUnsafe()) {
                Log.LogWarning("{Mode}.Resume: Engine failed, attempting reset", Mode);
                ResetEngineUnsafe();
                EnsureEngineRunningUnsafe();
            }
        }
        _isRunning.Invalidate();
    }

    public void Stop()
    {
        lock (_lock)
            StopUnsafe();
        _isRunning.Invalidate();
    }

    public void EnableVoiceProcessing()
    {
        lock (_lock) {
            if (_isVoiceProcessingEnabled)
                return;

            // Voice processing can be toggled only while the engine is stopped
            StopUnsafe();
            Input.SetVoiceProcessingEnabled(true);
            if (OperatingSystem.IsMacCatalyst()) {
                // VP ducks other apps' audio by default — unacceptable on desktop
                if (OperatingSystem.IsMacCatalystVersionAtLeast(17))
                    Input.DisableOtherAudioDucking();
                ConnectVoiceProcessingGraphUnsafe();
            }
            _isVoiceProcessingEnabled = true;
        }
        _isRunning.Invalidate();
    }

    public void DisableVoiceProcessing()
    {
        lock (_lock) {
            if (!_isVoiceProcessingEnabled)
                return;

            StopUnsafe();
            if (_vpCaptureMixer != null) {
                DisposeNodeUnsafe(_vpCaptureMixer);
                _vpCaptureMixer = null;
                _vpCaptureFormat = null;
            }
            Input.SetVoiceProcessingEnabled(false);
            _isVoiceProcessingEnabled = false;
        }
        _isRunning.Invalidate();
    }

    public IDisposable AttachVoiceProcessedInputSink(AVAudioSinkNodeReceiverHandler2 handler)
    {
        lock (_lock) {
            if (_vpCaptureMixer is null || _vpCaptureFormat is null)
                throw StandardError.Constraint("Voice processing graph is not initialized.");

            var sink = new AVAudioSinkNode(handler);
            _engine.AttachNode(sink);
            _engine.Connect(_vpCaptureMixer, sink, _vpCaptureFormat);
            return Disposable.New(() => {
                lock (_lock)
                    DisposeNodeUnsafe(sink);
            });
        }
    }

    public void AttachNode(AVAudioNode node)
    {
        lock (_lock)
            _engine.AttachNode(node);
    }

    public void Connect(AudioNode source, AudioNode target, AVAudioFormat format)
    {
        lock (_lock)
            _engine.Connect(source.Node, target.Node, format);
    }

    public void ConnectToMainInput(AudioNode node)
    {
        lock (_lock)
            _engine.Connect(_engine.InputNode, node.Node, _engine.InputNode.GetBusOutputFormat(AudioNode.Bus));
    }

    public void ConnectToMainMixer(AudioNode node, AVAudioFormat format)
    {
        lock (_lock)
            _engine.Connect(node.Node, _engine.MainMixerNode, format);
    }

    public void ConnectToMainMixer(AudioNode node)
    {
        lock (_lock)
            _engine.Connect(node.Node, _engine.MainMixerNode, _engine.MainMixerNode.GetBusInputFormat(AudioNode.Bus));
    }

    public MixerNode NewMixer()
    {
        var node = new AVAudioMixerNode();
        AttachNode(node);
        return new MixerNode(node, DisposeNode, Hub);
    }

    public PlayerNode NewPlayer(AVAudioFormat format, bool connectToMainOutput = true)
    {
        var node = new PlayerNode(new AVAudioPlayerNode(), format, DisposeNode, Hub);
        AttachNode(node.Node);
        if (connectToMainOutput)
            ConnectToMainMixer(node, format);
        return node;
    }

    // Private methods

    private void StopUnsafe()
    {
        // On Mac Catalyst AVAudioEngine.Stop alone doesn't tear down the VPIO
        // aggregate device's IO thread, which wedges the HAL transport and makes
        // the next engine start block for ~15s (kAUStartIO error -66681) —
        // stop the IO units explicitly first, as LiveKit does.
        if (OperatingSystem.IsMacCatalyst() && _isVoiceProcessingEnabled) {
            _engine.InputNode.AudioUnit?.Stop();
            _engine.OutputNode.AudioUnit?.Stop();
        }
        _engine.Stop();
        _wasStarted = false;
    }

    private void ConnectVoiceProcessingGraphUnsafe()
    {
        // Mac VPIO tolerates exactly one input puller: the sink attached via
        // AttachVoiceProcessedInputSink (a tap glitches it), the
        // MainMixer -> Output connection clocks the VP downlink and carries the
        // playback that AEC uses as its far-end reference, and formats are
        // explicit mono — see docs/plans/maccatalyst-voice-processing.md for why.
        var input = _engine.InputNode;
        var output = _engine.OutputNode;
        var inputFormat = input.GetBusOutputFormat(AudioNode.Bus);
        var outputFormat = output.GetBusOutputFormat(AudioNode.Bus);
        if (inputFormat.SampleRate == 0 || inputFormat.ChannelCount == 0
            || outputFormat.SampleRate == 0 || outputFormat.ChannelCount == 0)
            throw StandardError.Internal(
                $"Audio device formats aren't available yet (input: {inputFormat}, output: {outputFormat}).");

        var inputMono = new AVAudioFormat(inputFormat.CommonFormat, inputFormat.SampleRate, 1, inputFormat.Interleaved);
        var outputMono =
            new AVAudioFormat(outputFormat.CommonFormat, outputFormat.SampleRate, 1, outputFormat.Interleaved);
        var mixer = new AVAudioMixerNode();
        _engine.AttachNode(mixer);
        _engine.Connect(input, mixer, inputMono);
        _engine.Connect(_engine.MainMixerNode, output, outputMono);
        _vpCaptureMixer = mixer;
        _vpCaptureFormat = inputMono;
    }

    private void DisposeNode(AVAudioNode node)
    {
        lock (_lock)
            DisposeNodeUnsafe(node);
    }

    private void DisposeNodeUnsafe(AVAudioNode node)
    {
        _engine.DisconnectNodeInput(node);
        _engine.DisconnectNodeOutput(node);
        _engine.DetachNode(node);
        node.DisposeSilently();
    }

    private void EnsureEngineRunningUnsafe()
    {
        if (!_engine.Running) {
            Log.LogInformation("{Mode}.EnsureEngineRunningUnsafe: Engine not running, starting", Mode);
            if (OperatingSystem.IsMacCatalyst() && _isVoiceProcessingEnabled)
                StartWithVoiceProcessingUnsafe();
            else {
                _engine.StartAndReturnError(out var nsError);
                nsError.Assert();
            }
        }
        _wasStarted = true;
    }

    private void StartWithVoiceProcessingUnsafe()
    {
        // On Mac the engine start races the VPIO aggregate device build and can
        // fail with -10875 (also when another app holds VP); prepare, give it
        // a moment, and retry — same workaround LiveKit uses on macOS.
        _engine.Prepare();
        Thread.Sleep(100);
        NSError nsError = null!;
        for (var i = 0; i < 3; i++) {
            if (i > 0)
                Thread.Sleep(100);
            if (_engine.StartAndReturnError(out nsError))
                return;

            Log.LogWarning("{Mode}.StartWithVoiceProcessingUnsafe: attempt {Attempt} failed: {Error}",
                Mode, i + 1, nsError.LocalizedDescription);
        }
        nsError.Assert();
    }

    private bool TryEnsureEngineRunningUnsafe()
    {
        if (_engine.Running)
            return true;

        Log.LogInformation("{Mode}.TryEnsureEngineRunningUnsafe: Engine not running, attempting to start", Mode);
        if (!_engine.StartAndReturnError(out var nsError)) {
            Log.LogWarning("{Mode}.TryEnsureEngineRunningUnsafe: Failed to start: {Error}",
                Mode, nsError.LocalizedDescription);
            return false;
        }
        _wasStarted = true;
        return true;
    }

    private void ResetEngineUnsafe()
    {
        Log.LogInformation("{Mode}.ResetEngineUnsafe: Resetting engine", Mode);
        _engine.Stop();
        _engine.Reset();
    }

    private Task<bool> GetIsRunning(CancellationToken cancellationToken)
        => BackgroundTask.Run(() => {
            lock (_lock)
                return Task.FromResult(_engine.Running);
        }, cancellationToken);
}
