using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Locking;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class AudioNodes(AppUIHub hub) : IAsyncDisposable
{
    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private static readonly AVAudioFormat SoundFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, true);
    private static readonly AVAudioFormat FeederFormat = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, false);
    private AVAudioEngine _engine = null!;
    private bool _isInitialized;
    private bool _isDisposed;

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public ValueTask DisposeAsync()
        => MainThread.InvokeOnMainThreadAsync(() => {
                _isDisposed = true;
                if (!_isInitialized)
                    return;

                foreach (var node in _engine.AttachedNodes) {
                    try {
                        if (node is AVAudioPlayerNode playerNode)
                            playerNode.Stop();
                        _engine.DetachNode(node);
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
            })
            .ToValueTask();

    public async Task<SoundPlayerNode> CreateSoundNode()
    {
        await EnsureInitialized(CancellationToken.None).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() => new SoundPlayerNode(_engine, SoundFormat)).ConfigureAwait(false);
    }

    public async Task<BufferPlayerNode> CreateBufferNode()
    {
        await EnsureInitialized(CancellationToken.None).ConfigureAwait(false);
        return await MainThread.InvokeOnMainThreadAsync(() => new BufferPlayerNode(_engine, FeederFormat, hub)).ConfigureAwait(false);
    }

    private async Task EnsureInitialized(CancellationToken cancellationToken)
    {
        if (_isInitialized)
            return;

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
            return;

        Log.LogInformation("Activating audio session");
        IosAudioSessionHelper.ActivateRecordingAndBackgroundAudio();

        Log.LogInformation("Initializing audio engine");
        await MainThread.InvokeOnMainThreadAsync(() => {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _engine = new AVAudioEngine();
            })
            .ConfigureAwait(false);
        _isInitialized = true;
        Log.LogInformation("Audio engine initialized");
    }
}
