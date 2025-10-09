using ActualChat.Pooling;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Locking;
using ActualLab.Pooling;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class AudioEngines : IAsyncDisposable
{
    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private readonly SharedResourcePool<AudioMode, AudioEngine> _pool;
    private readonly AudioEngine _tunes;
    private readonly AudioEngine _playback;
    private readonly AudioEngine _recording;
    private readonly HashSet<AudioMode> _modes = [AudioMode.Tunes];
    private readonly Disposable<NSObject> _interruptionSubscription;
    private readonly Disposable<NSObject> _configurationChangeSubscription;

    private AppUIHub Hub { get; }
    [field: AllowNull, MaybeNull]
    private AudioSession AudioSession => field ??= Hub.Services.GetRequiredService<AudioSession>();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public AudioEngines(AppUIHub hub)
    {
        Hub = hub;
        _tunes = new AudioEngine(AudioMode.Tunes, hub);
        _playback = new AudioEngine(AudioMode.Playback, hub);
        _recording = new AudioEngine(AudioMode.Recording, hub);
        _pool = new SharedResourcePool<AudioMode, AudioEngine>(CreateAudioEngine, ReleaseEngine);
        _interruptionSubscription = Disposable.New(AVAudioSession.Notifications.ObserveInterruption(HandleInterruption),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _configurationChangeSubscription =
            Disposable.New(AVAudioEngine.Notifications.ObserveConfigurationChange(HandleConfigurationChange),
                NSNotificationCenter.DefaultCenter.RemoveObserver);
    }

    public async ValueTask DisposeAsync()
    {
        _interruptionSubscription.DisposeSilently();
        _configurationChangeSubscription.DisposeSilently();
        await _pool.DisposeSilentlyAsync();
        _tunes.DisposeSilently();
        _playback.DisposeSilently();
        _recording.DisposeSilently();
    }

    public async ValueTask<IResourceLease<AudioEngine>> Rent(AudioMode mode)
        => await _pool.Rent(mode).ConfigureAwait(false);

    private async Task<AudioEngine> CreateAudioEngine(AudioMode mode, CancellationToken cancellationToken)
    {
        using var _1 = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        if (mode > _modes.Max())
        {
            Pause();
            await AudioSession.Reconfigure(mode).ConfigureAwait(false);
            Resume(mode);
        }

        _modes.Add(mode);
        return mode switch {
            AudioMode.Tunes => _tunes,
            AudioMode.Playback => _playback,
            AudioMode.Recording => _recording,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private async ValueTask ReleaseEngine(AudioMode mode, AudioEngine engine)
    {
        using var _1 = await _lock.Lock().ConfigureAwait(false);
        if (mode is not AudioMode.Tunes && _modes.Remove(mode) && _modes.Max() < mode)
        {
            Pause();
            await AudioSession.Reconfigure(_modes.Max()).ConfigureAwait(false);
            Resume(mode);
        }
    }

    private void Pause()
    {
        _tunes.Pause();
        _playback.Pause();
        _recording.Pause();
    }

    private void Resume(AudioMode mode)
    {
        if (mode >= AudioMode.Tunes)
            _tunes.Resume();
        if (mode >= AudioMode.Playback)
            _playback.Resume();
        if (mode >= AudioMode.Recording)
            _recording.Resume();
    }

    private void HandleConfigurationChange(object? sender, NSNotificationEventArgs e)
        => Log.LogInformation("Audio engine configuration change");

    private void HandleInterruption(object? sender, AVAudioSessionInterruptionEventArgs e)
    {
        _ = BackgroundTask.Run(async () => {
                Log.LogInformation(
                    "Interruption type={Type}, reason={Reason}, wasSuspended={WasSuspended}, option={Option}",
                    e.InterruptionType,
                    e.Reason,
                    e.WasSuspended,
                    e.Option);
                using var _ = await _lock.Lock().ConfigureAwait(false);
                switch (e.InterruptionType) {
                case AVAudioSessionInterruptionType.Ended:
                    if (e.Option == AVAudioSessionInterruptionOptions.ShouldResume)
                        Resume(_modes.Max());
                    break;
                case AVAudioSessionInterruptionType.Began:
                    Pause();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
                }
            },
            Log,
            "Failed to handle interruption");
    }
}
