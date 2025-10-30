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
    private bool _isBluetoothScoActive;

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
        => RequestFocus(AudioFocus.Gain, AudioUsageKind.VoiceCommunication, AudioContentType.Speech);

    public bool RequestFocusForNotification()
        => RequestFocus(AudioFocus.GainTransientMayDuck, AudioUsageKind.AssistanceSonification, AudioContentType.Sonification);

    public void AbandonFocus()
    {
        if (_focusRequest == null)
            return;

        _log.LogInformation("Abandon audio focus");
        StopBluetoothSco();
        _audioManager.AbandonAudioFocusRequest(_focusRequest);
        _hasFocus = false;
    }

    public void ApplyPreferredRoute()
    {
        try {
            // If any external output device is connected (Bluetooth/Wired/USB), do NOT force speakerphone.
            // Otherwise force speakerphone so audio leaves through loud speaker (not earpiece).
            var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs) ?? [];

            // Separate A2DP and SCO detection
            var hasA2dp = devices.Any(d => d.Type == AudioDeviceType.BluetoothA2dp);
            var hasScoOutput = devices.Any(d => d.Type == AudioDeviceType.BluetoothSco);

            // Consider any external device (Bluetooth A2DP/SCO, wired, USB, etc.) as "not speakerphone"
            var hasExternal = hasA2dp || hasScoOutput || devices.Any(d => d.Type is AudioDeviceType.WiredHeadphones
                                               or AudioDeviceType.WiredHeadset
                                               or AudioDeviceType.UsbHeadset
                                               or AudioDeviceType.UsbDevice
                                               or AudioDeviceType.Hdmi
                                               or AudioDeviceType.LineAnalog
                                               or AudioDeviceType.LineDigital
                                               or AudioDeviceType.Dock);

            // Manage Bluetooth SCO connection ONLY when a SCO-capable device is present
            // Don't start SCO for A2DP-only devices
            if (hasScoOutput)
                StartBluetoothSco();
            else
                StopBluetoothSco();

            var shouldUseSpeaker = !hasExternal;
            if (_audioManager.SpeakerphoneOn != shouldUseSpeaker) {
                _audioManager.SpeakerphoneOn = shouldUseSpeaker;
                _log.LogInformation("Routing changed. SpeakerphoneOn={Speaker}", shouldUseSpeaker);
            }
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

    private void StartBluetoothSco()
    {
        if (_isBluetoothScoActive)
            return;

        try {
            if (!_audioManager.IsBluetoothScoAvailableOffCall)
                return;

            _audioManager.StartBluetoothSco();
            _audioManager.BluetoothScoOn = true;
            _isBluetoothScoActive = true;
            _log.LogInformation("Bluetooth SCO started");
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to start Bluetooth SCO");
        }
    }

    private void StopBluetoothSco()
    {
        if (!_isBluetoothScoActive)
            return;

        try {
            _audioManager.StopBluetoothSco();
            _audioManager.BluetoothScoOn = false;
            _isBluetoothScoActive = false;
            _log.LogInformation("Bluetooth SCO stopped");
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to stop Bluetooth SCO");
        }
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
