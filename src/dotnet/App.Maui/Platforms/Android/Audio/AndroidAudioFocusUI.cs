using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioFocusUI : MauiAudioFocusUI
{
    private readonly AndroidAudioFocusHelper _focusHelper;
    private MauiAudioFocusHandle? _handle;

    public AndroidAudioFocusUI(AppUIHub hub)
        : base(hub)
    {
        _focusHelper = new AndroidAudioFocusHelper(Platform.AppContext, hub.LogFor<AndroidAudioFocusHelper>());
        _focusHelper.OnFocusChanged += OnFocusChanged;
        _focusHelper.OnOutputDevicesChanged += OnOutputDevicesChanged;
    }

    public override Task TryRecover(CancellationToken cancellationToken = default)
    {
        Log.LogInformation("TryRecover: attempting to recover audio focus");
        _handle?.RaiseFocusRecover();
        return Task.CompletedTask;
    }

    protected override async Task<MauiAudioFocusHandle?> RequestAudioFocus(AudioFocusMode mode)
    {
        Log.LogInformation("-> RequestAudioFocus, requested mode: '{Mode}', active handle: '{Handle}'", mode, _handle);

        var success = mode switch {
            AudioFocusMode.Recording => await _focusHelper.RequestFocusForCallAsync().ConfigureAwait(false),
            AudioFocusMode.Tune => _focusHelper.RequestFocusForNotification(),
            _ => await _focusHelper.RequestFocusForPlaybackAsync().ConfigureAwait(false)
        };
        if (!success) {
            Log.LogInformation("Failed to get audio focus");
            _handle = null;
            return null;
        }

        var handle = new MauiAudioFocusHandle(OnRelease);
        _handle = handle;
        Log.LogInformation("-- RequestAudioFocus: Success. Active handle: {Handle}, mode: {Mode}", handle, mode);
        return handle;

        void OnRelease(MauiAudioFocusHandle self) {
            Log.LogInformation("AudioFocusHandle {Handle} releasing", self);
            // ReSharper disable once AccessToModifiedClosure
            if (_handle == self) {
                _focusHelper.AbandonFocus();
                _handle = null;
            }
        }
    }

    private void OnFocusChanged(AudioFocus af)
    {
        Log.LogInformation("-> OnFocusChanged: {AudioFocus}. Active handle: {Handle}", af, _handle);
        if (_handle == null)
            return;

        if (af is AudioFocus.LossTransient or AudioFocus.LossTransientCanDuck)
            _handle.RaiseFocusLost(true, af is AudioFocus.LossTransientCanDuck);
        else if (af is AudioFocus.Loss)
            _handle.RaiseFocusLost(false, false);
        if (af is AudioFocus.Gain or AudioFocus.GainTransient or AudioFocus.GainTransientExclusive)
            _handle.RaiseFocusRecover();
    }

    private void OnOutputDevicesChanged()
    {
        // Note: Audio routing is now handled internally by AudioFocusHelper's device router
        // when devices change during active focus. This callback is kept for logging/monitoring.
        Log.LogInformation("-> OnOutputDevicesChanged. Active handle: {Handle}", _handle);
    }
}
