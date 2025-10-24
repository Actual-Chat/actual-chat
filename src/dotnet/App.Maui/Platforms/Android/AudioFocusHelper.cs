using Android.Content;
using Android.Media;

namespace ActualChat.App.Maui;

public class AudioFocusHelper
{
    private readonly AudioManager _audioManager;
    private readonly AudioFocusChangeListener _audioFocusChangeListener;
    private readonly ILogger _log;
    private AudioFocusRequestClass? _focusRequest;
    private bool _hasFocus;

    public event Action<AudioFocus>? OnFocusChanged;

    public AudioFocusHelper(Context context, ILogger log)
    {
        _audioManager = (AudioManager)context.GetSystemService(Context.AudioService)!;
        _audioFocusChangeListener = new AudioFocusChangeListener(OnAudioFocusChange);
        _log = log;
    }

    public bool RequestFocusForCall()
        => RequestFocus(AudioFocus.GainTransientExclusive, AudioUsageKind.VoiceCommunication, AudioContentType.Speech);

    public bool RequestFocusForPlayback()
        => RequestFocus(AudioFocus.Gain, AudioUsageKind.Media, AudioContentType.Speech);

    public bool RequestFocusForNotification()
        => RequestFocus(AudioFocus.GainTransientMayDuck, AudioUsageKind.AssistanceSonification, AudioContentType.Sonification);

    public void AbandonFocus()
    {
        if (_focusRequest == null)
            return;

        _log.LogInformation("Abandon audio focus");
        _audioManager.AbandonAudioFocusRequest(_focusRequest);
        _hasFocus = false;
    }

    private bool RequestFocus(AudioFocus audioFocus, AudioUsageKind audioUsageKind, AudioContentType audioContentType)
    {
        var attrs = new AudioAttributes.Builder()
            .SetUsage(audioUsageKind)!
            .SetContentType(audioContentType)!
            .Build()!;

        _focusRequest = new AudioFocusRequestClass.Builder(audioFocus)
            .SetAudioAttributes(attrs)
            .SetOnAudioFocusChangeListener(_audioFocusChangeListener)
            .Build()!;

        var result = _audioManager.RequestAudioFocus(_focusRequest);
        _hasFocus = result == AudioFocusRequest.Granted;
        _log.LogInformation("Requested audio focus for '{Usage}', granted = {Result}", audioUsageKind, _hasFocus);
        return _hasFocus;
    }

    private void OnAudioFocusChange(AudioFocus focusChange)
    {
        _log.LogInformation("Audio focus change: {FocusChange}", focusChange);
        OnFocusChanged?.Invoke(focusChange);
    }

    // Nested types
    private class AudioFocusChangeListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly Action<AudioFocus> _onChange;

        public AudioFocusChangeListener(Action<AudioFocus> onChange)
            => _onChange = onChange;

        public void OnAudioFocusChange(AudioFocus focusChange)
            => _onChange(focusChange);
    }
}
