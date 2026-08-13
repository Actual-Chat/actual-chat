using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

// Each AVAudioEngine holds a virtual audio device with a live IO thread and DSP chain in
// mediaserverd, and neither Pause() nor Stop() gives it back - only deallocating the engine
// does. Measured on an iPhone 13 Pro: audiomxd runs three of them at ~0.3 cores after a call,
// with every engine stopped and the session deactivated, and they only go when the process
// exits. So the AVAudioEngine itself is built on first use and torn down once the last player
// node goes away.

/// <summary>
/// An <see cref="AVAudioEngine"/> that exists only while it has at least one player node.
/// </summary>
public class AudioEngine : IDisposable
{
    public static readonly AVAudioFormat VoicePlaybackFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.PlaybackSampleRate, 1, false);
    public static readonly AVAudioFormat VoiceRecordingFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.RecordingSampleRate, 1, false);

    private readonly Lock _lock = new ();
    private readonly ComputedState<bool> _isRunning;
    private readonly Debouncer<Unit> _idleReleaseDebouncer;
    private AVAudioEngine? _engine;
    private InputNode? _inputNode;
    private int _playerNodeCount;
    private bool _isStarted;
    private bool _isDisposed;
    private AudioFocusMode Mode { get; }
    private AppUIHub Hub { get;  }
    private ILogger Log => field ??= Hub.LogFor(GetType());
    public IState<bool> IsRunning => _isRunning;

    public InputNode Input {
        get {
            lock (_lock)
                return _inputNode ??= new InputNode(EngineUnsafe.InputNode, Hub);
        }
    }

    private AVAudioEngine EngineUnsafe => _engine ??= new AVAudioEngine();

    public AudioEngine(AudioFocusMode mode, AppUIHub hub, TimeSpan idleReleaseDelay = default)
    {
        Mode = mode;
        Hub = hub;
        _isRunning = hub.StateFactory.NewComputed(GetIsRunning, StateCategories.Get(GetType(), nameof(IsRunning)));
        _idleReleaseDebouncer = Debouncer.New<Unit>(idleReleaseDelay, ReleaseIfIdle);
    }

    public void Dispose()
    {
        lock (_lock) {
            if (_isDisposed)
                return;

            _isDisposed = true;
            ReleaseUnsafe();
        }
        _idleReleaseDebouncer.Reset();
        _isRunning.DisposeSilently();
    }

    public void EnsureRunning()
    {
        lock (_lock) {
            if (_isDisposed)
                return;

            EnsureEngineRunningUnsafe();
        }
        _isRunning.Invalidate();
    }

    public void Pause()
    {
        if (!_isStarted)
            return;

        lock (_lock) {
            if (!_isStarted)
                return;

            EngineUnsafe.Pause();
        }
        _isRunning.Invalidate();
    }

    public void Resume()
    {
        if (!_isStarted)
            return;

        lock (_lock) {
            if (!_isStarted)
                return;

            if (!TryEnsureEngineRunningUnsafe()) {
                Log.LogWarning("{Mode}.Resume: Engine failed, attempting reset", Mode);
                ResetEngineUnsafe();
                EnsureEngineRunningUnsafe();
            }
        }
        _isRunning.Invalidate();
    }

    // Tears the AVAudioEngine down rather than stopping it - see the note on this type. Safe to
    // call more than once, and it must never be reached through Input, which would rebuild the
    // very engine being released.
    public void Release()
    {
        lock (_lock)
            ReleaseUnsafe();
        _isRunning.Invalidate();
    }

    public void AttachNode(AVAudioNode node)
    {
        lock (_lock)
            EngineUnsafe.AttachNode(node);
    }

    public void Connect(AudioNode source, AudioNode target, AVAudioFormat format)
    {
        lock (_lock)
            EngineUnsafe.Connect(source.Node, target.Node, format);
    }

    public void ConnectToMainInput(AudioNode node)
    {
        lock (_lock) {
            var engine = EngineUnsafe;
            engine.Connect(engine.InputNode, node.Node, engine.InputNode.GetBusOutputFormat(AudioNode.Bus));
        }
    }

    public void ConnectToMainMixer(AudioNode node, AVAudioFormat format)
    {
        lock (_lock)
            EngineUnsafe.Connect(node.Node, EngineUnsafe.MainMixerNode, format);
    }

    public void ConnectToMainMixer(AudioNode node)
    {
        lock (_lock) {
            var engine = EngineUnsafe;
            engine.Connect(node.Node, engine.MainMixerNode, engine.MainMixerNode.GetBusInputFormat(AudioNode.Bus));
        }
    }

    public MixerNode NewMixer()
    {
        var node = new AVAudioMixerNode();
        AttachNode(node);
        return new MixerNode(node, DisposeNode, Hub);
    }

    public PlayerNode NewPlayer(AVAudioFormat format, bool connectToMainOutput = true)
    {
        var node = new PlayerNode(new AVAudioPlayerNode(), format, DisposePlayerNode, Hub);
        lock (_lock)
            _playerNodeCount++;
        AttachNode(node.Node);
        if (connectToMainOutput)
            ConnectToMainMixer(node, format);
        return node;
    }

    // Private methods

    private void DisposePlayerNode(AVAudioNode node)
    {
        bool isIdle;
        lock (_lock) {
            DisposeNodeUnsafe(node);
            isIdle = --_playerNodeCount == 0;
        }
        if (isIdle)
            _idleReleaseDebouncer.Debounce(default);
    }

    private Task ReleaseIfIdle(Unit _)
    {
        lock (_lock) {
            // Re-checked here rather than by cancelling the debounce, so a player created while
            // the release was pending keeps the engine alive without any ordering assumptions.
            if (_isDisposed || _engine is null || _playerNodeCount != 0)
                return Task.CompletedTask;

            Log.LogInformation("{Mode}.ReleaseIfIdle: no players left, releasing the engine", Mode);
            ReleaseUnsafe();
        }
        _isRunning.Invalidate();
        return Task.CompletedTask;
    }

    private void ReleaseUnsafe()
    {
        if (_engine is null)
            return;

        // Reached through the cached wrapper, never through Input - see Release().
        _inputNode?.Reset();
        _engine.Stop();
        // Before the engine, so the node is released while the graph that owns it is still there.
        _inputNode?.Dispose();
        _engine.DisposeSilently();
        _engine = null;
        _inputNode = null;
        _isStarted = false;
    }

    private void DisposeNode(AVAudioNode node)
    {
        lock (_lock)
            DisposeNodeUnsafe(node);
    }

    private void DisposeNodeUnsafe(AVAudioNode node)
    {
        if (_engine is { } engine) {
            engine.DisconnectNodeInput(node);
            engine.DisconnectNodeOutput(node);
            engine.DetachNode(node);
        }
        node.DisposeSilently();
    }

    private void EnsureEngineRunningUnsafe()
    {
        var engine = EngineUnsafe;
        if (!engine.Running) {
            Log.LogInformation("{Mode}.EnsureEngineRunningUnsafe: Engine not running, starting", Mode);
            engine.StartAndReturnError(out var nsError);
            nsError.Assert();
        }
        _isStarted = true;
    }

    private bool TryEnsureEngineRunningUnsafe()
    {
        var engine = EngineUnsafe;
        if (engine.Running)
            return true;

        Log.LogInformation("{Mode}.TryEnsureEngineRunningUnsafe: Engine not running, attempting to start", Mode);
        if (!engine.StartAndReturnError(out var nsError)) {
            Log.LogWarning("{Mode}.TryEnsureEngineRunningUnsafe: Failed to start: {Error}", Mode, nsError.LocalizedDescription);
            return false;
        }
        _isStarted = true;
        return true;
    }

    private void ResetEngineUnsafe()
    {
        Log.LogInformation("{Mode}.ResetEngineUnsafe: Resetting engine", Mode);
        EngineUnsafe.Stop();
        EngineUnsafe.Reset();
    }

    private Task<bool> GetIsRunning(CancellationToken cancellationToken)
        => BackgroundTask.Run(() => {
            lock (_lock)
                return Task.FromResult(_engine?.Running ?? false);
        }, cancellationToken);
}
