using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Kotlin.Jvm.Functions;
using Xamarin.Twilio.AudioSwitch;
using static Android.Media.AudioManager;

namespace ActualChat.App.Maui;

internal class AndroidAudioOutputController : IAudioOutputController
{
    private const string AndroidAudioOutput = nameof(AndroidAudioOutput);
    private readonly AudioSwitch _audioSwitch;
    private readonly AudioManager _audioManager;
    private bool _isActivated;
    private readonly IStoredState<bool> _isSpeakerphoneOnStored;

    public AndroidAudioOutputController(IServiceProvider services)
    {
        _audioManager = (AudioManager)Platform.AppContext.GetSystemService(Context.AudioService)!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S) {
            try {
                _audioManager.AddOnModeChangedListener(Platform.AppContext.MainExecutor!, new ModeChangedListener());
            }
            catch(Exception e) {
                Log.Warn(AndroidConstants.LogTag, Java.Lang.Throwable.FromException(e), "Failed to add ModeChangedListener");
            }
        }
        _audioSwitch = new AudioSwitch(Platform.AppContext, true, new FocusChangeListener());
        _audioSwitch.Start(new StartupCallback());
        var stateFactory = services.StateFactory();
        IsSpeakerphoneOn = stateFactory.NewComputed<bool>((_, _) => Task.FromResult(IsSpeakerphoneOnInternal()));

        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(AndroidAudioOutput));
        _isSpeakerphoneOnStored = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsSpeakerphoneOn)) {
                InitialValue = false
            });
    }

    public IState<bool> IsSpeakerphoneOn { get; }

    // TODO(DF):
    // We need to have an audio player that can playback opus audio with AudioUsageKind.VoiceCommunication and AudioContentType.Speech
    // standard audio context player from web view playbacks audio AudioUsageKind.Media.
    // It cause the following problems when InCommunication mode is enabled:
    // 1) Media stream audio sounds a bit quieter than in Normal mode;
    // 2) There 2 volume controls on a screen: one for media, the second one for loudspeaker/earpiece.
    // Hardware volume buttons affects second one even if I set Activity.VolumeControlStream = Android.Media.Stream.Music.
    // Audio volume is actually controlled only by the first one which appears on a screen after pressing hardware volume control button.
    //
    // TODO(DF):
    // May be I can get rid of AudioSwitch. But I need to test how _audioManager.SetCommunicationDevice works.
    // This is API 31 level. Which is available since Android 12.
    public void ToggleAudioDevice(bool enableAudioDevice)
    {
        Log.Debug(AndroidConstants.LogTag, "Toggle audio switch: " + enableAudioDevice);
        if (enableAudioDevice) {
            _audioSwitch.Activate();
            _isActivated = true;

            Log.Debug(AndroidConstants.LogTag, "AudioManager.Mode: " + _audioManager.Mode);
            Log.Debug(AndroidConstants.LogTag, "AudioManager.SpeakerphoneOn: " + _audioManager.SpeakerphoneOn);

            SetSpeakerphoneOnInternal(_isSpeakerphoneOnStored.Value);
        }
        else {
            _audioSwitch.Deactivate();
            _isActivated = false;
        }
    }

    public void SwitchSpeakerphone()
    {
        Log.Debug(AndroidConstants.LogTag, "About to switch Speakerphone");
        Log.Debug(AndroidConstants.LogTag, $"AudioManager.Mode: {_audioManager.Mode}");
        if (_isActivated) {
            Log.Debug(AndroidConstants.LogTag, "Force setting InCommunication mode.");
            _audioManager.Mode = Mode.InCommunication;
        }
        Log.Debug(AndroidConstants.LogTag, $"Selected audio device: {_audioSwitch.SelectedAudioDevice}");
        var isSpeakerphoneOn = !IsSpeakerphoneOnInternal();
        SetSpeakerphoneOnInternal(isSpeakerphoneOn);
        _isSpeakerphoneOnStored.Value = isSpeakerphoneOn;
    }

    private void SetSpeakerphoneOnInternal(bool isSpeakerphoneOn)
    {
        if (isSpeakerphoneOn) {
            var devices = _audioSwitch.AvailableAudioDevices;
            var speakerphone = devices.FirstOrDefault(c => c is AudioDevice.Speakerphone);
            _audioSwitch.SelectDevice(speakerphone);
        }
        else {
            // force device auto selection, speakerphone has lowest priority, so headset or earpiece should be selected
            _audioSwitch.SelectDevice(null);
        }
        InvalidateSelectedDeviceComputed();
    }

    private bool IsSpeakerphoneOnInternal()
        => _audioSwitch.SelectedAudioDevice is AudioDevice.Speakerphone;

    private void InvalidateSelectedDeviceComputed()
    {
        using (Computed.Invalidate())
            IsSpeakerphoneOn.Invalidate();
    }

    private class StartupCallback : Java.Lang.Object, IFunction2
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? devices, Java.Lang.Object? selectedDevice)
            => null;
    }

    private class FocusChangeListener : Java.Lang.Object, IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange([GeneratedEnum] AudioFocus focusChange)
            // TODO(DF): handle audio focus change
            => Log.Debug(AndroidConstants.LogTag, $"AudioFocus changed: {focusChange}");
    }

    private class ModeChangedListener : Java.Lang.Object, IOnModeChangedListener
    {
        public void OnModeChanged(int mode)
             => Log.Debug(AndroidConstants.LogTag, $"AudioManager.Mode change: {(Mode)mode}");
    }
}
