using Android.Content;
using Android.Media;
using OperationCanceledException = System.OperationCanceledException;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Low-level Android audio focus helper. All public methods assume sequential (serialized)
/// calling — synchronization is handled by <see cref="AndroidAudioFocusUI"/> via AsyncLock.
/// </summary>
public class AndroidAudioFocusHelper : IDisposable
{
    private readonly AudioManager _audioManager;
    private readonly AudioFocusChangeListener _audioFocusChangeListener;
    private readonly DeviceCallback _deviceCallback;
    private readonly ILogger _log;
    private readonly IAudioDeviceRouter _deviceRouter;
    private AudioFocusRequestClass? _focusRequest;
    private bool _hasFocus;

    public event Action<AudioFocus>? OnFocusChanged;
    public event Action? OnOutputDevicesChanged;

    public AndroidAudioFocusHelper(Context context, ILogger log)
    {
        _log = log;
        _audioManager = (AudioManager)context.GetSystemService(Context.AudioService)!;
        _audioFocusChangeListener = new AudioFocusChangeListener(OnAudioFocusChange);
        _deviceCallback = new DeviceCallback(OnAudioDevicesChanged);
        _audioManager.RegisterAudioDeviceCallback(_deviceCallback, null);

        // Chooses the implementation based on API level
        // API 31 (Android 12) introduced SetCommunicationDevice
        _deviceRouter = CreateDeviceRouter(_audioManager, context, log);
    }

    private static IAudioDeviceRouter CreateDeviceRouter(AudioManager audioManager, Context context, ILogger log)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(12))
            try {
                return new ModernAudioDeviceRouter(audioManager, log);
            }
            catch (Exception e) {
                log.LogError(e, "Failed to create ModernAudioDeviceRouter, falling back to LegacyAudioDeviceRouter");
            }

        return new LegacyAudioDeviceRouter(audioManager, context, log);
    }

    public void Dispose()
    {
        try {
            AbandonFocus();
        }
        catch (Exception e) {
            _log.LogError(e, "Failed to abandon audio focus during disposal");
        }
        _deviceRouter.Dispose();
        _audioFocusChangeListener.Dispose();
        _audioManager.UnregisterAudioDeviceCallback(_deviceCallback);
    }

    public Task<bool> RequestFocusForCall()
        => RequestFocusAsync(AudioFocus.GainTransient, AudioUsageKind.VoiceCommunication, AudioContentType.Speech);

    public Task<bool> RequestFocusForPlayback()
        => RequestFocusAsync(AudioFocus.Gain, AudioUsageKind.VoiceCommunication, AudioContentType.Speech);

    public Task<bool> RequestFocusForNotification()
        => RequestFocusAsync(AudioFocus.GainTransientMayDuck, AudioUsageKind.AssistanceSonification, AudioContentType.Sonification);

    public async Task WarmUpAudioMode()
    {
        if (_hasFocus || _audioManager.Mode == Mode.InCommunication)
            return; // Already in communication mode, nothing to warm up
        if (_audioManager.IsMusicActive)
            return; // Another app is playing audio, skip warmup to avoid interruption
        if (IncomingCallRinger.IsPlaying)
            // Priming the comm pipeline flips the mode to InCommunication, which reroutes the
            // ringtone (STREAM_RING) speaker->earpiece->speaker — an audible drop mid-ring.
            return;

        _log.LogInformation("WarmUpAudioMode: briefly switching to InCommunication to prime audio HAL");
        _audioManager.Mode = Mode.InCommunication;
        // Give the HAL time to load the communication audio pipeline
        await Task.Delay(300).ConfigureAwait(false);
        // Only revert if no real audio focus was acquired during warmup
        if (!_hasFocus) {
            _audioManager.Mode = Mode.Normal;
            _log.LogInformation("WarmUpAudioMode: reverted to Normal");
        }
        else
            _log.LogInformation("WarmUpAudioMode: keeping InCommunication (real focus acquired)");
    }

    public void AbandonFocus()
    {
        if (_focusRequest == null)
            return;

        _log.LogInformation("Abandon audio focus");
        _deviceRouter.ClearCommunicationDevice();
        _audioManager.AbandonAudioFocusRequest(_focusRequest);
        _audioManager.Mode = Mode.Normal;
        _hasFocus = false;
    }

    private async Task<bool> RequestFocusAsync(AudioFocus audioFocus, AudioUsageKind audioUsageKind, AudioContentType audioContentType)
    {
        var isCommunication = audioUsageKind == AudioUsageKind.VoiceCommunication;

        // For voice communication, we need to set Mode.InCommunication to enable proper audio routing.
        // Note: This mode defaults to earpiece on many devices, but the device router will handle
        // selecting the appropriate output device (BT headset, wired headset, or speakerphone).
        if (isCommunication)
            _audioManager.Mode = Mode.InCommunication;

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

        // After gaining focus, apply routing preference (handles external devices like Bluetooth)
        if (_hasFocus && isCommunication)
            await _deviceRouter.SetCommunicationDeviceAsync(CancellationToken.None).ConfigureAwait(false);

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

        // Re-route audio if we have active focus in communication mode
        // This handles: BT connected mid-recording, BT disconnected, etc.
        if (_hasFocus && _audioManager.Mode == Mode.InCommunication)
            _ = HandleDevicesChangedAsync();
    }

    private async Task HandleDevicesChangedAsync()
    {
        try {
            await _deviceRouter.OnDevicesChangedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to re-route audio after device change");
        }
    }

    // ========== Device Router Interface ==========

    private interface IAudioDeviceRouter : IDisposable
    {
        /// <summary>
        /// Select the best available communication device (BT > Wired > Speaker).
        /// Waits for device to be ready before returning.
        /// </summary>
        Task<bool> SetCommunicationDeviceAsync(CancellationToken ct);

        /// <summary>
        /// Clear communication device selection, restore default routing.
        /// </summary>
        void ClearCommunicationDevice();

        /// <summary>
        /// Called when audio devices change (connect/disconnect).
        /// Re-evaluates and updates routing if needed.
        /// </summary>
        Task OnDevicesChangedAsync(CancellationToken ct);
    }

    // ========== Modern Implementation (API 31+) ==========

    private class ModernAudioDeviceRouter : IAudioDeviceRouter
    {
        private readonly AudioManager _audioManager;
        private readonly ILogger _log;
        private CommunicationDeviceListener? _listener;

        public ModernAudioDeviceRouter(AudioManager audioManager, ILogger log)
        {
            _audioManager = audioManager;
            _log = log;

            // Register listener for device changes
            _listener = new CommunicationDeviceListener(log);
            _audioManager.AddOnCommunicationDeviceChangedListener(
                Platform.AppContext.MainExecutor!,
                _listener);
        }

        public Task<bool> SetCommunicationDeviceAsync(CancellationToken ct)
        {
            try {
                var devices = _audioManager.AvailableCommunicationDevices;

                _log.LogInformation("Available communication devices: {Devices}",
                    string.Join(", ", devices.Select(d => d.Type.ToString())));

                // Priority: BLE Headset > BT SCO > Wired > USB > Speaker (NOT earpiece!)
                // When no external device is connected, we MUST select BuiltinSpeaker
                // because Mode.InCommunication defaults to earpiece
                var device = devices.FirstOrDefault(d => d.Type == AudioDeviceType.BleHeadset)
                          ?? devices.FirstOrDefault(d => d.Type == AudioDeviceType.BluetoothSco)
                          ?? devices.FirstOrDefault(d => d.Type == AudioDeviceType.WiredHeadset)
                          ?? devices.FirstOrDefault(d => d.Type == AudioDeviceType.WiredHeadphones)
                          ?? devices.FirstOrDefault(d => d.Type == AudioDeviceType.UsbHeadset)
                          ?? devices.FirstOrDefault(d => d.Type == AudioDeviceType.BuiltinSpeaker);

                if (device == null) {
                    _log.LogWarning("No communication devices available, audio may route to earpiece");
                    return Task.FromResult(false);
                }

                // Short-circuit: if the current communication device is already the desired type, skip
                var currentDevice = _audioManager.CommunicationDevice;
                if (currentDevice != null && currentDevice.Type == device.Type) {
                    _log.LogInformation("Communication device already set to: {Type}", device.Type);
                    return Task.FromResult(true);
                }

                _log.LogInformation("Setting communication device to: {Type}", device.Type);

                // SetCommunicationDevice returns true if the OS accepted the routing request.
                // The OnCommunicationDeviceChanged callback is just a confirmation notification
                // that can arrive with significant delay on some devices — no need to await it.
                var success = _audioManager.SetCommunicationDevice(device);

                if (!success)
                    _log.LogWarning("SetCommunicationDevice returned false for device: {Type}", device.Type);
                else
                    _log.LogInformation("Communication device set to: {Type} (confirmed: {Actual})",
                        device.Type, _audioManager.CommunicationDevice?.Type);

                return Task.FromResult(success);
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to set communication device");
                return Task.FromResult(false);
            }
        }

        public void ClearCommunicationDevice()
        {
            try {
                _audioManager.ClearCommunicationDevice();
                _log.LogInformation("Communication device cleared");
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to clear communication device");
            }
        }

        public Task OnDevicesChangedAsync(CancellationToken ct)
            // Modern API: just re-select best device when devices change
            => SetCommunicationDeviceAsync(ct);

        public void Dispose()
        {
            if (_listener == null)
                return;

            try {
                _audioManager.RemoveOnCommunicationDeviceChangedListener(_listener);
            }
            catch { /* Ignore */ }
            _listener = null;
        }

    }

    // Legacy Implementation (API 28-30)
    // Uses deprecated APIs that are required for backward compatibility with older Android versions.
    // These APIs are obsoleted on Android 34+ but we only use this class on API 28-30.
#pragma warning disable CA1422 // Validate platform compatibility

    private class LegacyAudioDeviceRouter : IAudioDeviceRouter
    {
        private readonly AudioManager _audioManager;
        private readonly Context _context;
        private readonly ILogger _log;
        private readonly ScoStateReceiver _scoReceiver;
        private TaskCompletionSource<bool>? _pendingScoConnection;
        private bool _isBluetoothScoActive;

        public LegacyAudioDeviceRouter(AudioManager audioManager, Context context, ILogger log)
        {
            _audioManager = audioManager;
            _context = context;
            _log = log;

            // Register BroadcastReceiver for SCO state
            _scoReceiver = new ScoStateReceiver(this);
            var filter = new IntentFilter(AudioManager.ActionScoAudioStateUpdated);
            _context.RegisterReceiver(_scoReceiver, filter);
        }

        public async Task<bool> SetCommunicationDeviceAsync(CancellationToken ct)
        {
            try {
                var devices = _audioManager.GetDevices(GetDevicesTargets.Outputs) ?? [];

                _log.LogInformation("Available output devices (legacy): {Devices}",
                    string.Join(", ", devices.Select(d => d.Type.ToString())));

                // Check for any external device (BT, wired, USB)
                var hasBluetooth = devices.Any(d => d.Type is AudioDeviceType.BluetoothA2dp
                                                           or AudioDeviceType.BluetoothSco);
                var hasWired = devices.Any(d => d.Type is AudioDeviceType.WiredHeadset
                                                       or AudioDeviceType.WiredHeadphones
                                                       or AudioDeviceType.UsbHeadset);

                // Try Bluetooth SCO first
                if (hasBluetooth && _audioManager.IsBluetoothScoAvailableOffCall) {
                    _log.LogInformation("Attempting to connect Bluetooth SCO");

                    _pendingScoConnection = new TaskCompletionSource<bool>();

                    if (!_isBluetoothScoActive) {
                        _audioManager.StartBluetoothSco();
                        _audioManager.BluetoothScoOn = true;
                        _isBluetoothScoActive = true;
                    }

                    // Wait for SCO_AUDIO_STATE_CONNECTED (max 2 seconds)
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(2000);
                    try {
                        var connected = await _pendingScoConnection.Task.WaitAsync(cts.Token).ConfigureAwait(false);
                        if (connected) {
                            _audioManager.SpeakerphoneOn = false; // BT connected, disable speaker
                            _log.LogInformation("Bluetooth SCO connected, speakerphone disabled");
                            return true;
                        }
                    }
                    catch (OperationCanceledException) {
                        _log.LogWarning("SCO connection timeout, checking if already connected");
                        // SCO might already be connected, check speakerphone state
                        if (_audioManager.BluetoothScoOn) {
                            _audioManager.SpeakerphoneOn = false;
                            _log.LogInformation("Bluetooth SCO was already on, speakerphone disabled");
                            return true;
                        }
                    }
                }

                // If wired headset connected, disable speakerphone (audio goes to headset)
                if (hasWired) {
                    _audioManager.SpeakerphoneOn = false;
                    _log.LogInformation("Wired headset detected, speakerphone disabled");
                    return true;
                }

                // No external device - USE SPEAKERPHONE (not earpiece!)
                // This is critical: Mode.InCommunication defaults to earpiece
                _audioManager.SpeakerphoneOn = true;
                _log.LogInformation("No external audio device, using speakerphone");
                return true;
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to set communication device (legacy)");
                // Fallback to speakerphone
                _audioManager.SpeakerphoneOn = true;
                return true;
            }
        }

        public void ClearCommunicationDevice()
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

        public Task OnDevicesChangedAsync(CancellationToken ct)
            // Legacy API: re-apply routing when devices change
            // This handles BT connecting mid-recording
            => SetCommunicationDeviceAsync(ct);

        public void Dispose()
        {
            try {
                _context.UnregisterReceiver(_scoReceiver);
            }
            catch { /* Ignore */ }

            ClearCommunicationDevice();
        }

        private class ScoStateReceiver(LegacyAudioDeviceRouter parent) : BroadcastReceiver
        {
            public override void OnReceive(Context? context, Intent? intent)
            {
                var state = intent?.GetIntExtra(AudioManager.ExtraScoAudioState, -1);
                parent._log.LogInformation("SCO state changed: {State}", state);

                // SCO_AUDIO_STATE_CONNECTED = 1, SCO_AUDIO_STATE_DISCONNECTED = 0
                if (state == 1) // Connected
                    parent._pendingScoConnection?.TrySetResult(true);
                else if (state == 0) // Disconnected
                    parent._pendingScoConnection?.TrySetResult(false);
            }
        }
    }

#pragma warning restore CA1422 // Validate platform compatibility

    // Nested Types

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
