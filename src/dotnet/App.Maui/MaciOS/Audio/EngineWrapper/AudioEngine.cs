using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class AudioEngine : IDisposable
{
    public static readonly AVAudioFormat VoicePlaybackFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.PlaybackSampleRate, 1, false);
    public static readonly AVAudioFormat VoiceRecordingFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.RecordingSampleRate, 1, false);

    private readonly Lock _lock = new ();
    private readonly AVAudioEngine _engine = new ();
    private bool _wasStarted;
    private readonly ComputedState<bool> _isRunning;
    private AudioFocusMode Mode { get; }
    private AppUIHub Hub { get;  }
    private ILogger Log => field ??= Hub.LogFor(GetType());
    public IState<bool> IsRunning => _isRunning;

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
        lock (_lock) {
            _engine.Stop();
            _wasStarted = false;
        }
        _isRunning.Invalidate();
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
            _engine.StartAndReturnError(out var nsError);
            nsError.Assert();
        }
        _wasStarted = true;
    }

    private bool TryEnsureEngineRunningUnsafe()
    {
        if (_engine.Running)
            return true;

        Log.LogInformation("{Mode}.TryEnsureEngineRunningUnsafe: Engine not running, attempting to start", Mode);
        if (!_engine.StartAndReturnError(out var nsError)) {
            Log.LogWarning("{Mode}.TryEnsureEngineRunningUnsafe: Failed to start: {Error}", Mode, nsError.LocalizedDescription);
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
