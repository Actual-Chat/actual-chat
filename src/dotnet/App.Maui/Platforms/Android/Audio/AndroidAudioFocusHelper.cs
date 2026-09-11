using Android.Content;
using Android.Media;
using OperationCanceledException = System.OperationCanceledException;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Low-level Android audio focus helper. All public methods assume sequential (serialized)
/// calling — synchronization is handled by <see cref="AndroidAudioFocusUI"/> via AsyncLock.
/// </summary>
public sealed class AndroidAudioFocusHelper : IDisposable
{
    private static readonly TimeSpan NonSilenceMemory = TimeSpan.FromSeconds(5);

    private readonly AudioManager _audioManager;
    private readonly AudioFocusChangeListener _audioFocusChangeListener;
    private readonly DeviceCallback _deviceCallback;
    private readonly ILogger _log;
    private readonly IAudioDeviceRouter _deviceRouter;
    private AudioFocusRequestClass? _focusRequest;
    private bool _hasFocus;
    private bool _isCommunicationFocus;
    private bool _isCommunicationModeYielded;
    private bool _isSilentStart;
    // _hasFocus minus the focus we lost to another app - i.e. is the session we measured still ours
    private bool _isFocusSessionOpen;
    private CpuTimestamp _lastNonSilentAt = CpuTimestamp.Now - TimeSpan.FromHours(1);
    public bool IsCommunicationFocus => _isCommunicationFocus;

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

    public Task<bool> RequestFocusForCall(bool useCommunicationRoute)
        // Without the communication route we never open SCO - and opening SCO outside a real
        // call is an HFP virtual call, which makes a car head unit take over its screen.
        => useCommunicationRoute
            ? RequestFocus(AudioFocus.GainTransient, AudioUsageKind.VoiceCommunication, AudioContentType.Speech)
            : RequestFocus(AudioFocus.GainTransient, AudioUsageKind.Media, AudioContentType.Speech);

    public Task<bool> RequestFocusForPlayback()
        // Playback needs no microphone, and the communication route drops a BT peer to SCO - a virtual call.
        => RequestFocus(AudioFocus.Gain, AudioUsageKind.Media, AudioContentType.Speech);

    public Task<bool> RequestFocusForListening()
        // Transient, not Gain: this focus lives only while someone's speech is actually playing,
        // so whatever it pauses - music, navigation - auto-resumes once the utterance ends.
        // ResolveGain still escalates it to a permanent gain when nothing was playing to resume.
        => RequestFocus(AudioFocus.GainTransient, AudioUsageKind.Media, AudioContentType.Speech);

    public Task<bool> RequestFocusForProjectedMedia()
        // Gain, not transient: gearhead skips a transient request when the phone still holds the
        // tune's guidance-only transient at the head unit, so the media channel never opens and
        // playback ends up on the guidance channel over a ducked radio. A Gain is always forwarded.
        => RequestFocus(AudioFocus.Gain, AudioUsageKind.Media, AudioContentType.Speech);

    public Task<bool> RequestFocusForNotification()
        => RequestFocus(
            AudioFocus.GainTransientMayDuck,
            AudioUsageKind.AssistanceSonification,
            AudioContentType.Sonification);

    public async Task WarmUpAudioMode(bool isProjectionActive)
    {
        if (isProjectionActive)
            return; // Priming the comm pipeline flips the mode, which is exactly what we avoid in a car

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
        // Give the HAL time to load the communication audio pipeline (~300ms), but poll for an
        // incoming ring meanwhile: on a cold start the ringtone can start inside this window, and
        // InCommunication reroutes it (STREAM_RING) to the earpiece. Bail out the instant it does.
        // There's no active call during warmup (guarded by _hasFocus), so reverting is always safe.
        for (var i = 0; i < 10; i++) {
            await Task.Delay(30).ConfigureAwait(false);
            if (IncomingCallRinger.IsPlaying) {
                _audioManager.Mode = Mode.Normal;
                _log.LogInformation("WarmUpAudioMode: aborted (incoming ringtone started)");
                return;
            }
        }
        // Only revert if no real audio focus was acquired during warmup
        if (!_hasFocus) {
            _audioManager.Mode = Mode.Normal;
            _log.LogInformation("WarmUpAudioMode: reverted to Normal");
        }
        else {
            _log.LogInformation("WarmUpAudioMode: keeping InCommunication (real focus acquired)");
        }
    }

    public Task EnsureCommunicationRoute(CancellationToken cancellationToken)
        // Deliberately not driven off the device-changed callback: Android re-clears the route
        // every ~6s for as long as a focus holder stays idle, and answering each one turned into a
        // permanent re-assert loop (13 in 100s, measured). Asserting only when audio is about to
        // play fixes the route where it matters and leaves an idle armed session alone.
        => _isCommunicationFocus
            ? _deviceRouter.SelectCommunicationDevice(cancellationToken)
            : Task.FromResult(false);

    public Task<bool> SelectBuiltinSpeaker(CancellationToken cancellationToken)
        // Deliberately bypasses the priority list in SelectCommunicationDevice: a Bluetooth device
        // here would raise an HFP virtual call, which is what pinning playback to the phone avoids.
        => _deviceRouter.SelectBuiltinSpeaker(cancellationToken);

    public void YieldCommunicationMode()
    {
        LogAudioState();

        // An armed session holds InCommunication with no call in sight, so the ring borrows Normal back.
        if (_isCommunicationModeYielded || _audioManager.Mode != Mode.InCommunication)
            return;

        _log.LogInformation("Yielding the communication mode to the incoming ring");
        _audioManager.Mode = Mode.Normal;
        _isCommunicationModeYielded = true;
    }

    public async Task RestoreCommunicationMode()
    {
        if (!_isCommunicationModeYielded)
            return;

        _isCommunicationModeYielded = false;
        if (!_hasFocus)
            return; // The focus went away while ringing - whoever takes it next sets the mode

        _log.LogInformation("Restoring the communication mode after the incoming ring");
        _audioManager.Mode = Mode.InCommunication;
        await _deviceRouter.SelectCommunicationDevice(CancellationToken.None).ConfigureAwait(false);
    }

    public void AbandonFocus()
    {
        if (_focusRequest == null)
            return;

        _log.LogInformation("Abandon audio focus");
        if (!_isSilentStart)
            // Whatever this session interrupted is about to resume on the gain we hand back, and it
            // needs a moment to start playing - IsSilentStart must not read that gap as silence.
            _lastNonSilentAt = CpuTimestamp.Now;
        _deviceRouter.ClearCommunicationDevice();
        _audioManager.AbandonAudioFocusRequest(_focusRequest);
        _audioManager.Mode = Mode.Normal;
        _hasFocus = false;
        Volatile.Write(ref _isFocusSessionOpen, false);
        _isCommunicationFocus = false;
    }

    // Private methods

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

    private async Task<bool> RequestFocus(
        AudioFocus audioFocus,
        AudioUsageKind audioUsageKind,
        AudioContentType audioContentType)
    {
        LogAudioState();
        // Resolved ahead of the mode change below, which is what the silence check reads.
        var gain = ResolveGain(audioFocus, audioUsageKind);
        var isCommunication = audioUsageKind == AudioUsageKind.VoiceCommunication;

        // For voice communication, we need to set Mode.InCommunication to enable proper audio routing.
        // Note: This mode defaults to earpiece on many devices, but the device router will handle
        // selecting the appropriate output device (BT headset, wired headset, or speakerphone).
        if (isCommunication)
            _audioManager.Mode = Mode.InCommunication;
        else if (_isCommunicationFocus) {
            // A renewal off the communication route: nothing abandons in between, so the mode
            // and the SCO route would outlive the focus that asked for them. Keyed on our own
            // state: AudioManager.Mode read Normal here while SCO was still up on 2026-09-09.
            // A mode yielded to a ring has nothing to come back to either, or the restore would
            // reopen SCO under a media focus.
            _log.LogInformation("Leaving the communication route: dropping SCO, restoring Mode.Normal");
            _deviceRouter.ClearCommunicationDevice();
            _audioManager.Mode = Mode.Normal;
            _isCommunicationModeYielded = false;
        }

        var attrs = new AudioAttributes.Builder()
            .SetUsage(audioUsageKind)!
            .SetContentType(audioContentType)!
            .Build()!;

        _focusRequest = new AudioFocusRequestClass.Builder(gain)
            .SetAudioAttributes(attrs)
            .SetOnAudioFocusChangeListener(_audioFocusChangeListener)
            .Build()!;

        var result = _audioManager.RequestAudioFocus(_focusRequest);
        _hasFocus = result == AudioFocusRequest.Granted;
        _isCommunicationFocus = _hasFocus && isCommunication;
        Volatile.Write(ref _isFocusSessionOpen, _hasFocus);
        _log.LogInformation("Requested audio focus for '{Usage}' ({Gain}), granted = {Result}",
            audioUsageKind, gain, _hasFocus);
        if (isCommunication && !_hasFocus) {
            // The mode was raised before the request, and a denial - the normal answer during a
            // real phone call, which is exactly when a PTT wake arrives - used to leave it on
            // InCommunication with no focus. Nothing resets it after that: AbandonFocus is
            // unreachable because the failure path nulls _focusRequest, so ringtones and media
            // kept routing to the earpiece until some later full cycle happened to fix it.
            _log.LogWarning("Audio focus denied for '{Usage}' - restoring Mode.Normal", audioUsageKind);
            _audioManager.Mode = Mode.Normal;
        }

        // After gaining focus, apply routing preference (handles external devices like Bluetooth)
        if (_hasFocus && isCommunication)
            await _deviceRouter.SelectCommunicationDevice(CancellationToken.None).ConfigureAwait(false);

        return _hasFocus;
    }

    private AudioFocus ResolveGain(AudioFocus gain, AudioUsageKind audioUsageKind)
    {
        // Measured once per focus session: our own playback counts as active audio, so a renewal
        // mid-utterance would answer differently than the acquire that opened the session.
        if (!Volatile.Read(ref _isFocusSessionOpen))
            _isSilentStart = IsSilentStart();

        // The tune keeps its transient gain even in silence: under projection gearhead answers it
        // with a guidance-only focus, and what a permanent one does to the media request that
        // follows is untested - see docs/live-audio/11-android-auto.md. It doesn't need the
        // escalation anyway: a permanent gain drops the previous owner off the focus stack, so
        // once recording or listening has taken one there is nobody left for the tune to hand back to.
        if (gain == AudioFocus.Gain || audioUsageKind == AudioUsageKind.AssistanceSonification)
            return gain;

        // Abandoning a transient focus hands an AUDIOFOCUS_GAIN back to the app that held it before
        // us, and media apps resume on it even when the user paused them long before our session
        // began - so a session that started in silence asks for a permanent gain instead, which
        // evicts that app with an AUDIOFOCUS_LOSS and leaves nothing to resume.
        return _isSilentStart ? AudioFocus.Gain : gain;
    }

    private bool IsSilentStart()
    {
        // IsMusicActive sees STREAM_MUSIC only, so a mode other than Normal - another app's VoIP
        // call, a ringtone - stands in for the streams it cannot report.
        if (_audioManager.IsMusicActive || _audioManager.Mode != Mode.Normal) {
            _lastNonSilentAt = CpuTimestamp.Now;
            return false;
        }

        // Listening abandons its focus between utterances, and the app we hand the gain back to
        // takes a moment to start playing again - silence just after audio we did hear is that
        // resume gap, not evidence that nothing is playing.
        return _lastNonSilentAt.Elapsed >= NonSilenceMemory;
    }

    private void OnAudioFocusChange(AudioFocus focusChange)
    {
        _log.LogInformation("Audio focus change: {FocusChange}", focusChange);
        // Another app owns the audio from here on, so the session we measured is over and a
        // renewal must measure again - by then whatever took the focus is the audio to preserve.
        // A transient loss - a ring, a nav prompt - keeps it, since our session resumes after.
        if (focusChange == AudioFocus.Loss)
            Volatile.Write(ref _isFocusSessionOpen, false);

        OnFocusChanged?.Invoke(focusChange);
    }

    private void OnAudioDevicesChanged()
    {
        _log.LogInformation("Output audio devices changed");
        OnOutputDevicesChanged?.Invoke();

        // Re-route audio if we have active focus in communication mode
        // This handles: BT connected mid-recording, BT disconnected, etc.
        if (_isCommunicationFocus)
            _ = HandleDevicesChanged();
    }

    private async Task HandleDevicesChanged()
    {
        try {
            await _deviceRouter.OnDevicesChanged(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) {
            _log.LogWarning(e, "Failed to re-route audio after device change");
        }
    }

    private void LogAudioState()
    {
        try {
            var outputs = _audioManager.GetDevices(GetDevicesTargets.Outputs) ?? [];
            var commDevice = OperatingSystem.IsAndroidVersionAtLeast(12)
                ? _audioManager.CommunicationDevice?.Type
                : null;
            _log.LogInformation(
                "Audio state: mode={Mode}, hasFocus={HasFocus}, isYielded={IsYielded}, commDevice={CommDevice}, "
                + "ringerMode={RingerMode}, isMusicActive={IsMusicActive}, outputs=[{Outputs}]",
                _audioManager.Mode, _hasFocus, _isCommunicationModeYielded,
                commDevice, _audioManager.RingerMode, _audioManager.IsMusicActive,
                string.Join(", ", outputs.Select(d => d.Type.ToString())));
        }
        catch (Exception e) {
            _log.LogWarning(e, "Couldn't read the audio state");
        }
    }

    // Nested types

    private interface IAudioDeviceRouter : IDisposable
    {
        // Completes only once the route has actually landed, not when it's requested.
        Task<bool> SelectCommunicationDevice(CancellationToken ct);
        Task<bool> SelectBuiltinSpeaker(CancellationToken ct);
        void ClearCommunicationDevice();
        Task OnDevicesChanged(CancellationToken ct);
    }

    private sealed class ModernAudioDeviceRouter : IAudioDeviceRouter
    {
        // ~300ms budget, matching WarmUpAudioMode's own wait for the communication pipeline.
        private const int RouteSettleChecks = 10;
        private const int RouteSettleCheckPeriod = 30;

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

        public async Task<bool> SelectCommunicationDevice(CancellationToken ct)
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
                    return false;
                }

                return await SetAndAwaitCommunicationDevice(device, ct).ConfigureAwait(false);
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to set communication device");
                return false;
            }
        }

        public async Task<bool> SelectBuiltinSpeaker(CancellationToken ct)
        {
            try {
                var device = _audioManager.AvailableCommunicationDevices
                    .FirstOrDefault(d => d.Type == AudioDeviceType.BuiltinSpeaker);
                if (device == null) {
                    _log.LogWarning("Built-in speaker not available among communication devices");
                    return false;
                }

                return await SetAndAwaitCommunicationDevice(device, ct).ConfigureAwait(false);
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to set built-in speaker as communication device");
                return false;
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

        public Task OnDevicesChanged(CancellationToken ct)
            // Modern API: just re-select best device when devices change
            => SelectCommunicationDevice(ct);

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

        // Private methods

        private async Task<bool> SetAndAwaitCommunicationDevice(AudioDeviceInfo device, CancellationToken ct)
        {
            var currentDevice = _audioManager.CommunicationDevice;
            if (currentDevice == null || currentDevice.Type != device.Type) {
                _log.LogInformation("Setting communication device to: {Type}", device.Type);
                if (!_audioManager.SetCommunicationDevice(device))
                    _log.LogWarning("SetCommunicationDevice returned false for device: {Type}", device.Type);
            }

            // SetCommunicationDevice only accepts the request - the route lands later, and a
            // cold Normal -> InCommunication transition takes ~300ms to settle. Returning early
            // lets the first AudioTrack be created while the earpiece is still selected, and a
            // started track doesn't reliably follow a later switch: that's the wake-path bug
            // where the first utterance played out of the earpiece for its whole duration.
            var isRouted = await WhenCommunicationDeviceIs(device.Type, ct).ConfigureAwait(false);
            if (!isRouted)
                _log.LogWarning("Communication device didn't become {Type} in time (now: {Actual})",
                    device.Type, _audioManager.CommunicationDevice?.Type);
            return isRouted;
        }

        private async Task<bool> WhenCommunicationDeviceIs(AudioDeviceType type, CancellationToken ct)
        {
            for (var i = 0; i < RouteSettleChecks; i++) {
                if (_audioManager.CommunicationDevice?.Type == type)
                    return true;

                await Task.Delay(RouteSettleCheckPeriod, ct).ConfigureAwait(false);
            }
            return _audioManager.CommunicationDevice?.Type == type;
        }
    }

    // Uses APIs obsoleted on Android 34+, which is safe because this router only runs on API 28-30.
#pragma warning disable CA1422 // Validate platform compatibility

    private sealed class LegacyAudioDeviceRouter : IAudioDeviceRouter
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

        public async Task<bool> SelectCommunicationDevice(CancellationToken ct)
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

                    _pendingScoConnection = TaskCompletionSourceExt.New<bool>();

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

        public Task<bool> SelectBuiltinSpeaker(CancellationToken ct)
        {
            try {
                ClearCommunicationDevice(); // Drop any active SCO route - we want the speaker, not Bluetooth
                _audioManager.SpeakerphoneOn = true;
                _log.LogInformation("Forcing built-in speaker (legacy)");
                return Task.FromResult(true);
            }
            catch (Exception e) {
                _log.LogWarning(e, "Failed to force built-in speaker (legacy)");
                return Task.FromResult(false);
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

        public Task OnDevicesChanged(CancellationToken ct)
            // Legacy API: re-apply routing when devices change
            // This handles BT connecting mid-recording
            => SelectCommunicationDevice(ct);

        public void Dispose()
        {
            try {
                _context.UnregisterReceiver(_scoReceiver);
            }
            catch { /* Ignore */ }

            ClearCommunicationDevice();
        }

        private sealed class ScoStateReceiver(LegacyAudioDeviceRouter parent) : BroadcastReceiver
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

    private sealed class AudioFocusChangeListener(Action<AudioFocus> onChange)
        : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange)
            => onChange(focusChange);
    }

    private sealed class DeviceCallback(Action onChanged) : AudioDeviceCallback
    {
        public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices)
            => onChanged();
        public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices)
            => onChanged();
    }
}
