using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class AudioNodes(AppUIHub hub) : IDisposable
{
    public static readonly AVAudioFormat SoundFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, true);
    public static readonly AVAudioFormat FeederFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, false);

    private readonly Lock _lock = new();
    private AVAudioEngine _engine = null!;
    private bool _isInitialized;
    private bool _isDisposed;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public void Dispose()
    {
        _isDisposed = true;
        if (!_isInitialized)
            return;

        lock (_lock) {
            if (_isDisposed)
                return;

            foreach (var node in _engine.AttachedNodes) {
                try {
                    if (node is AVAudioPlayerNode playerNode)
                        DisposeNodeUnsafe(playerNode);
                }
                catch {
                    // ignored
                }
            }
            try {
                _engine.Stop();
                _engine.DisposeSilently();
            }
            catch {
                // ignored
            }
        }
    }

    private void DisposeNode(AVAudioPlayerNode node)
    {
        lock (_lock)
            DisposeNodeUnsafe(node);
    }

    private void DisposeNodeUnsafe(AVAudioPlayerNode node)
    {
        node.Stop();
        _engine.DisconnectNodeInput(node);
        _engine.DisconnectNodeOutput(node);
        _engine.DetachNode(node);
        node.DisposeSilently();
    }

    public ThreadSafePlayerNode CreatePlayerNode(AVAudioFormat format)
    {
        EnsureInitialized();
        lock (_lock)
            return CreatePlayerNodeUnsafe(format);
    }

    // TODO: rename or extract
    public BufferPlayerNode CreateBufferNode()
    {
        EnsureInitialized();
        lock (_lock)
            return new BufferPlayerNode(CreatePlayerNodeUnsafe(FeederFormat), FeederFormat, hub);
    }

    private void EnsureEngineRunningUnsafe()
    {
        lock (_lock)
            if (!_engine.Running) {
                Log.LogInformation("Engine not running, starting");
                _engine.Prepare();
                _engine.StartAndReturnError(out var nsError);
                nsError.Assert();
            }
    }

    private ThreadSafePlayerNode CreatePlayerNodeUnsafe(AVAudioFormat format)
    {
        var node = new AVAudioPlayerNode();
        _engine.AttachNode(node);
        _engine.Connect(node, _engine.MainMixerNode, format);
        EnsureEngineRunningUnsafe();
        return new ThreadSafePlayerNode(node, DisposeNode);
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_lock) {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isInitialized)
                return;

            Log.LogInformation("Activating audio session");
            IosAudioSessionHelper.ActivateRecordingAndBackgroundAudio();

            Log.LogInformation("Initializing audio engine");
            _engine = new AVAudioEngine();
            _isInitialized = true;
        }
    }
}
