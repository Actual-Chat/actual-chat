using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using Android.Media;

namespace ActualChat.App.Maui;

public class AndroidAudioFocusService : MauiAudioFocusService
{
    private readonly AudioFocusHelper _focusHelper;
    private long _idSeed;
    private AudioFocusHandle? _handle;

    public AndroidAudioFocusService(IServiceProvider services)
        : base(services)
    {
        _focusHelper = new AudioFocusHelper(Platform.AppContext, services.LogFor<AudioFocusHelper>());
        _focusHelper.OnFocusChanged += OnFocusChanged;
    }

    protected override Task<AudioFocusHandle?> RequestAudioFocus(AudioFocusConsumerKind mode)
    {
        Log.LogInformation("-> RequestAudioFocus, mode: {Mode}", mode);
        if (_handle != null) {
            Log.LogInformation("About to abandon active audio focus. Active focus handle id: {Id}", _handle.Id);
            _focusHelper.AbandonFocus();
            _handle = null;
        }

        var success = mode == AudioFocusConsumerKind.Recording
            ? _focusHelper.RequestFocusForCall()
            : _focusHelper.RequestFocusForPlayback();
        if (!success) {
            Log.LogInformation("Failed to get audio focus");
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
            _handle.RaiseLostFocus(true);
        else if (af is AudioFocus.Loss)
            _handle.RaiseLostFocus(false);
        if (af is AudioFocus.Gain or AudioFocus.GainTransient or AudioFocus.GainTransientExclusive)
            _handle.RaiseRecoverFocus();
    }
}
