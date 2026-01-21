using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Media;

namespace ActualChat.App.Maui;

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

    protected override Task<AudioFocusHandle?> RequestAudioFocus(AudioMode mode)
    {
        Log.LogInformation("-> RequestAudioFocus, requested mode: '{Mode}', active focus handle id: '{Id}'", mode, _handle?.Id);

        var success = mode switch
        {
            AudioMode.Recording => _focusHelper.RequestFocusForCall(),
            AudioMode.Tunes => _focusHelper.RequestFocusForNotification(),
            _ => _focusHelper.RequestFocusForPlayback()
        };
        if (!success) {
            Log.LogInformation("Failed to get audio focus");
            _handle = null;
            return Task.FromResult<AudioFocusHandle?>(null);
        }

        var id = Interlocked.Increment(ref _idSeed);
        var handle = new AudioFocusHandle(id, OnRelease);
        _handle = handle;
        Log.LogInformation("-- RequestFocusForPlayback: Success. Active focus handle id: {Id}, mode: {Mode}", handle.Id, mode);
        return Task.FromResult<AudioFocusHandle?>(handle);

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
        Log.LogInformation("-> OnOutputDevicesChanged. Active focus handle id: {Id}", _handle?.Id);
        if (_handle != null)
            _focusHelper.ApplyPreferredRoute();
    }
}
