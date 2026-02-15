using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.Locking;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class IosAudioFocusUI : MauiAudioFocusUI
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.2, 3);

    private readonly AsyncLock _lock = new (LockReentryMode.CheckedFail);
    private readonly ConcurrentDictionary<AudioFocusMode, AudioFocusMode> _modes = new () {
        [AudioFocusMode.Tune] = AudioFocusMode.Tune,
    };
    private readonly Disposable<NSObject> _interruptionSubscription;
    private readonly Disposable<NSObject> _configurationChangeSubscription;
    private MauiAudioFocusHandle? _handle;

    private AudioSession AudioSession => field ??= Hub.Services.GetRequiredService<AudioSession>();
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();

    public IosAudioFocusUI(AppUIHub hub) : base(hub)
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

    public override async Task TryRecover(CancellationToken cancellationToken = default)
    {
        using var cancellationToken1 = cancellationToken.LinkWith(StopToken);
        await AsyncChain.From(RecoverInternal)
            .Retry(RetryDelays, 3, Log)
            .LogError(Log)
            .Run(StopToken)
            .ConfigureAwait(false);
    }

    protected override async Task<MauiAudioFocusHandle?> RequestAudioFocus(AudioFocusMode mode)
    {
        _modes.TryAdd(mode, mode);
        await SetModeUnsafe(mode).ConfigureAwait(false);
        _handle = new MauiAudioFocusHandle(x => _ = Release(mode, x));
        return _handle;
    }

    // Private methods

    private async Task Release(AudioFocusMode mode, MauiAudioFocusHandle handle)
    {
        Log.LogInformation("AudioFocusHandle {Handle} releasing", handle);
        using var _1 = await _lock.Lock(StopToken).ConfigureAwait(false);
        try {
            if (mode is AudioFocusMode.Recording)
                AudioEngines.Recording.StopRecording();
            if (mode is not AudioFocusMode.Tune && _modes.TryRemove(mode, out _))
                await SetModeUnsafe(_modes.Keys.Max()).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(StopToken))
                Log.LogError(e, "Failed to release audio focus");
        }
    }

    private async Task SetModeUnsafe(AudioFocusMode mode)
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
            "Failed to handle interruption",
            StopToken);
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
    {
        Log.LogInformation("Audio engine configuration change detected");
        _ = BackgroundTask.Run(async () => {
            using var _ = await _lock.Lock(StopToken).ConfigureAwait(false);
            if (_handle != null) {
                var currentMode = _modes.Keys.DefaultIfEmpty(AudioFocusMode.Tune).Max();
                AudioEngines.Resume(currentMode);
            }
        }, Log, "Failed to handle configuration change", StopToken);
    }

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
            _handle?.RaiseFocusLost(true, false);
            break;
        case AVAudioSessionInterruptionType.Ended:
            // Always attempt recovery - ShouldResume flag is unreliable for phone calls
            await TryRecover().ConfigureAwait(false);
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid interruption type");
        }
    }

    private async Task RecoverInternal(CancellationToken cancellationToken)
    {
        var currentMode = _modes.Keys.DefaultIfEmpty(AudioFocusMode.Tune).Max();
        await AudioSession.Reactivate(currentMode).ConfigureAwait(false);
        AudioEngines.Resume(currentMode);
        _handle?.RaiseFocusRecover();
    }
}
