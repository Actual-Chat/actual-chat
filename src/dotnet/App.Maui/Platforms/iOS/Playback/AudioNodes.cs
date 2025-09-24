using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class AudioNodes(AppUIHub hub) : IDisposable
{
    private static readonly AVAudioFormat SoundFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, true);
    private static readonly AVAudioFormat FeederFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, false);
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

        AudioDispatcher.Invoke(() => {
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
        });
    }

    public void DisposeNode(AVAudioPlayerNode node)
        => AudioDispatcher.Invoke(() => { DisposeNodeUnsafe(node); });

    private void DisposeNodeUnsafe(AVAudioPlayerNode node)
    {
        node.Stop();
        _engine.DisconnectNodeInput(node);
        _engine.DisconnectNodeOutput(node);
        _engine.DetachNode(node);
        node.DisposeSilently();
    }

    public SoundPlayerNode CreateSoundNode()
    {
        EnsureInitialized();
        return AudioDispatcher.Invoke(() => new SoundPlayerNode(CreatePlayerNode(), SoundFormat, hub));
    }

    public BufferPlayerNode CreateBufferNode()
    {
        EnsureInitialized();
        return AudioDispatcher.Invoke(() => {
            var node = CreatePlayerNode();
            return new BufferPlayerNode(node, FeederFormat, hub);
        });
    }

    public void EnsureNodePlaying(AVAudioPlayerNode node)
        => AudioDispatcher.Invoke(() => {
            EnsureEngineRunningUnsafe();
            if (!node.Playing)
                node.Play();
        });

    private AVAudioPlayerNode CreatePlayerNode()
    {
        var node = new AVAudioPlayerNode();
        _engine.AttachNode(node);
        _engine.Connect(node, _engine.MainMixerNode, FeederFormat);
        return node;
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
            return;

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        AudioDispatcher.Invoke(() => {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isInitialized)
                return;

            Log.LogInformation("Activating audio session");
            IosAudioSessionHelper.ActivateRecordingAndBackgroundAudio();

            Log.LogInformation("Initializing audio engine");
            _engine = new AVAudioEngine();
            _isInitialized = true;
        });
    }

    private void EnsureEngineRunningUnsafe()
    {
        if (!_engine.Running) {
            Log.LogInformation("Engine not running, starting");
            _engine.Prepare();
            _engine.StartAndReturnError(out var nsError);
            nsError.Assert();
        }
    }
}
