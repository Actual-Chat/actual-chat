using System;
using System.Linq;
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
    public event Action? OnOutputDevicesChanged;

    public AudioFocusHelper(Context context, ILogger log)
    {
        _audioManager = (AudioManager)context.GetSystemService(Context.AudioService)!;
        _audioFocusChangeListener = new AudioFocusChangeListener(OnAudioFocusChange);
        _audioManager.RegisterAudioDeviceCallback(new DeviceCallback(OnAudioDevicesChanged), null);
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

    public void ApplyPreferredRoute()
    {
        try {
            // If any external output device is connected (Bluetooth/Wired/USB), do NOT force speakerphone.
            // Otherwise force speakerphone so audio leaves through loud speaker (not earpiece).
            var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs) ?? [];
            var hasExternal = devices.Any(d => d.Type is AudioDeviceType.BluetoothA2dp
                                               or AudioDeviceType.BluetoothSco
                                               or AudioDeviceType.WiredHeadphones
                                               or AudioDeviceType.WiredHeadset
                                               or AudioDeviceType.UsbHeadset
                                               or AudioDeviceType.UsbDevice
                                               or AudioDeviceType.Hdmi
                                               or AudioDeviceType.LineAnalog
                                               or AudioDeviceType.LineDigital
                                               or AudioDeviceType.Dock);

            var shouldUseSpeaker = !hasExternal;
            if (_audioManager.SpeakerphoneOn == shouldUseSpeaker)
                return;

            _audioManager.SpeakerphoneOn = shouldUseSpeaker;
            _log.LogInformation("Routing changed. SpeakerphoneOn={Speaker}", shouldUseSpeaker);
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to apply preferred audio route");
        }
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

        // After gaining focus, apply routing preference
        if (_hasFocus)
            ApplyPreferredRoute();

        return _hasFocus;
    }

    private void OnAudioFocusChange(AudioFocus focusChange)
    {
        _log.LogInformation("Audio focus change: {FocusChange}", focusChange);
        OnFocusChanged?.Invoke(focusChange);
    }

    private void OnAudioDevicesChanged()
    {
        _log.LogInformation("Output audio devices changed");
        OnOutputDevicesChanged?.Invoke();
        ApplyPreferredRoute();
    }

    // Nested types
    private class AudioFocusChangeListener(Action<AudioFocus> onChange)
        : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange)
            => onChange(focusChange);
    }

    private class DeviceCallback(Action onChanged) : AudioDeviceCallback
    {
        public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices)
            => onChanged();
        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices)
            => onChanged();
    }
}
