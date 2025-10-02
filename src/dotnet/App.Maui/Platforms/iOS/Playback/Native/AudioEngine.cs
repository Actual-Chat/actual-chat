using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class AudioEngine
{
    public static readonly AVAudioFormat VoicePlaybackFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.PlaybackSampleRate, 1, false);
    public static readonly AVAudioFormat VoiceRecordingFormat = new (AVAudioCommonFormat.PCMFloat32, Constants.Audio.RecordingSampleRate, 1, false);

    private readonly Lock _lock = new ();
    private readonly AVAudioEngine _engine = new ();
    private readonly AppUIHub _hub;
    public InputNode Input { get; }

    public AudioEngine(AppUIHub hub)
    {
        _hub = hub;
        Input = new InputNode(_engine.InputNode);
    }

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= _hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public void EnsureRunning()
    {
        lock (_lock)
            EnsureEngineRunningUnsafe();
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
        return new MixerNode(node, DisposeNode);
    }

    public PlayerNode NewPlayer(AVAudioFormat format, bool connectToMainOutput = true)
    {
        var node = new PlayerNode(new AVAudioPlayerNode(), format, DisposeNode);
        AttachNode(node.Node);
        if (connectToMainOutput)
            ConnectToMainMixer(node, format);
        return node;
    }

    public void Prepare()
    {
        lock (_lock)
            _engine.Prepare();
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
        IosAudioSessionHelper.ActivateRecordingAndBackgroundAudio();
        lock (_lock)
            if (!_engine.Running) {
                Log.LogInformation("Engine not running, starting");
                _engine.StartAndReturnError(out var nsError);
                nsError.Assert();
            }
    }
}
