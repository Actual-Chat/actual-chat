using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Media;

namespace ActualChat.App.Maui.Audio;

public class AndroidAudioFocusService : MauiAudioFocusService
{
    private readonly AudioFocusHelper _focusHelper;
    private long _idSeed;
    private AudioFocusHandle? _handle;

    public AndroidAudioFocusService(AppUIHub hub)
        : base(hub)
    {
        _focusHelper = new AudioFocusHelper(Platform.AppContext, hub.LogFor<AudioFocusHelper>());
        _focusHelper.OnFocusChanged += OnFocusChanged;
        _focusHelper.OnOutputDevicesChanged += OnOutputDevicesChanged;
    }

    public override Task Recover(CancellationToken cancellationToken = default)
    {
        Log.LogInformation("Recover: attempting to recover audio focus");
        _handle?.RaiseRecoverFocus();
        return Task.CompletedTask;
    }

    protected override async Task<AudioFocusHandle?> RequestAudioFocus(AudioMode mode)
    {
        Log.LogInformation("-> RequestAudioFocus, requested mode: '{Mode}', active focus handle id: '{Id}'", mode, _handle?.Id);

        var success = mode switch {
            AudioMode.Recording => await _focusHelper.RequestFocusForCallAsync().ConfigureAwait(false),
            AudioMode.Tunes => _focusHelper.RequestFocusForNotification(),
            _ => await _focusHelper.RequestFocusForPlaybackAsync().ConfigureAwait(false)
        };
        if (!success) {
            Log.LogInformation("Failed to get audio focus");
            _handle = null;
            return null;
        }

        var id = Interlocked.Increment(ref _idSeed);
        var handle = new AudioFocusHandle(id, OnRelease);
        _handle = handle;
        Log.LogInformation("-- RequestAudioFocus: Success. Active focus handle id: {Id}, mode: {Mode}", handle.Id, mode);
        return handle;

        void OnRelease(AudioFocusHandle self)
        {
            Log.LogInformation("AudioFocusHandle '{Id}' releasing", self.Id);
            // ReSharper disable once AccessToModifiedClosure
            if (_handle == self) {
                _focusHelper.AbandonFocus();
                _handle = null;
            }
        }
    }

    private void OnFocusChanged(AudioFocus af)
    {
        Log.LogInformation("-> OnFocusChanged: {AudioFocus}. Active focus handle id: {Id}", af, _handle?.Id);
        if (_handle == null)
            return;

        if (af is AudioFocus.LossTransient or AudioFocus.LossTransientCanDuck)
            _handle.RaiseLostFocus(true, af is AudioFocus.LossTransientCanDuck);
        else if (af is AudioFocus.Loss)
            _handle.RaiseLostFocus(false, false);
        if (af is AudioFocus.Gain or AudioFocus.GainTransient or AudioFocus.GainTransientExclusive)
            _handle.RaiseRecoverFocus();
    }

    private void OnOutputDevicesChanged()
    {
        // Note: Audio routing is now handled internally by AudioFocusHelper's device router
        // when devices change during active focus. This callback is kept for logging/monitoring.
        Log.LogInformation("-> OnOutputDevicesChanged. Active focus handle id: {Id}", _handle?.Id);
    }
}
