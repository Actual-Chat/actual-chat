using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Locking;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class IosAudioFocusService : MauiAudioFocusService
{
    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private readonly ConcurrentDictionary<AudioMode, AudioMode> _modes = new () {
        [AudioMode.Tunes] = AudioMode.Tunes,
    };
    private readonly Disposable<NSObject> _interruptionSubscription;
    private readonly Disposable<NSObject> _configurationChangeSubscription;
    private int _id;
    private AudioFocusHandle? _handle;

    [field: AllowNull, MaybeNull]
    private AudioSession AudioSession => field ??= Hub.Services.GetRequiredService<AudioSession>();
    [field: AllowNull, MaybeNull]
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();

    public IosAudioFocusService(AppUIHub hub) : base(hub)
    {
        _interruptionSubscription = Disposable.New(AVAudioSession.Notifications.ObserveInterruption(OnInterruption),
            NSNotificationCenter.DefaultCenter.RemoveObserver);
        _configurationChangeSubscription =
            Disposable.New(AVAudioEngine.Notifications.ObserveConfigurationChange(OnConfigurationChange),
                NSNotificationCenter.DefaultCenter.RemoveObserver);
    }

    protected override async Task DisposeAsyncCore()
    {
        _interruptionSubscription.DisposeSilently();
        _configurationChangeSubscription.DisposeSilently();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    protected override async Task<AudioFocusHandle?> RequestAudioFocus(AudioMode mode)
    {
        _modes.TryAdd(mode, mode);
        await SetModeUnsafe(mode).ConfigureAwait(false);
        _handle = new AudioFocusHandle(Interlocked.Increment(ref _id), x => _ = Release(mode, x));
        return _handle;
    }

    private async Task Release(AudioMode mode, AudioFocusHandle handle)
    {
        Log.LogInformation("AudioFocusHandle '{Id}' releasing", handle.Id);
        using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
        try {
            if (mode is AudioMode.Recording)
                AudioEngines.Recording.StopRecording();
            if (mode is not AudioMode.Tunes && _modes.TryRemove(mode, out _))
                await SetModeUnsafe(_modes.Keys.Max()).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(StopToken))
                Log.LogError(e, "Failed to release audio focus");
        }
    }

    private async Task SetModeUnsafe(AudioMode mode)
    {
        AudioEngines.Pause();
        await AudioSession.Reconfigure(mode).ConfigureAwait(false);
        AudioEngines.Resume(mode);
    }

    private void OnInterruption(object? sender, AVAudioSessionInterruptionEventArgs e)
    {
        // IMPORTANT: event args must be captured by value otherwise they will change !!!!
        var type = e.InterruptionType;
        var reason = e.Reason;
        var wasSuspended = e.WasSuspended;
        var option = e.Option;
        _ = BackgroundTask.Run(() => HandleInterruption(type, reason, wasSuspended, option),
            Log,
            "Failed to handle interruption");
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
        => Log.LogInformation("Audio engine configuration change");

    private async Task HandleInterruption(AVAudioSessionInterruptionType type,
        AVAudioSessionInterruptionReason reason, bool? wasSuspended, AVAudioSessionInterruptionOptions option)
    {
        Log.LogInformation(
            "Interruption type={Type}, reason={Reason}, wasSuspended={WasSuspended}, option={Option}",
            type,
            reason,
            wasSuspended,
            option);
        using var _ = await _lock.Lock(StopToken).ConfigureAwait(false);
        switch (type) {
        case AVAudioSessionInterruptionType.Began:
            _handle?.RaiseLostFocus(true);
            break;
        case AVAudioSessionInterruptionType.Ended:
            if (option == AVAudioSessionInterruptionOptions.ShouldResume)
                _handle?.RaiseRecoverFocus();
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid interruption type");
        }
    }
}
