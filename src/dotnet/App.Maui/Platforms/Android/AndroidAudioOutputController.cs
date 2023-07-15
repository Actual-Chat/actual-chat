using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Kotlin.Jvm.Functions;
using Xamarin.Twilio.AudioSwitch;
using static Android.Media.AudioManager;

namespace ActualChat.App.Maui;

public sealed class AndroidAudioOutputController : IAudioOutputController
{
    private const string AndroidAudioOutput = nameof(AndroidAudioOutput);
    private readonly AudioSwitch _audioSwitch;
    private readonly AudioManager _audioManager;
    private readonly IMutableState<bool> _isAudioOn;
    private readonly IStoredState<bool> _isSpeakerphoneOnStored;
    private readonly object _lock = new();

    public IState<bool> IsAudioOn => _isAudioOn;
    public IState<bool> IsSpeakerphoneOn { get; }
    private ILogger Log { get; }

    public AndroidAudioOutputController(IServiceProvider services)
    {
        _audioManager = (AudioManager)Platform.AppContext.GetSystemService(Context.AudioService)!;
        Log = services.LogFor(GetType());

        if (Build.VERSION.SdkInt >= BuildVersionCodes.S) {
            try {
                _audioManager.AddOnModeChangedListener(
                    Platform.AppContext.MainExecutor!,
                    new ModeChangedListener(services.LogFor<ModeChangedListener>()));
            }
            catch(Exception e) {
                Log.LogWarning(e, "Failed to add ModeChangedListener");
            }
        }
        _audioSwitch = new AudioSwitch(
            Platform.AppContext, true,
            new FocusChangeListener(services.LogFor<FocusChangeListener>()));
        _audioSwitch.Start(new StartupCallback());

        var stateFactory = services.StateFactory();
        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(AndroidAudioOutput));
        var type = GetType();
        _isAudioOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsAudioOn)));
        _isSpeakerphoneOnStored = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsSpeakerphoneOn)) {
                InitialValue = false,
                Category = StateCategories.Get(type, nameof(_isSpeakerphoneOnStored)),
            });
        IsSpeakerphoneOn = stateFactory.NewComputed(
            new ComputedState<bool>.Options() {
                ComputedOptions = new ComputedOptions() {
                    AutoInvalidationDelay = TimeSpan.FromSeconds(5),
                },
                UpdateDelayer = FixedDelayer.ZeroUnsafe,
                Category = StateCategories.Get(type, nameof(IsSpeakerphoneOn)),
            },
            (_, _) => Task.FromResult(IsSpeakerphoneActuallyOn(true)));
    }

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
    public async ValueTask<bool> ToggleAudio(bool mustEnable)
    {
        lock (_lock) {
            if (_isAudioOn.Value == mustEnable)
                return mustEnable;

            Log.LogDebug("ToggleAudio({MustEnable})", mustEnable);
            if (mustEnable) {
                _audioSwitch.Activate();
                _isAudioOn.Value = true;
                TryEnterCommunicationMode();
            }
            else {
                _audioSwitch.Deactivate();
                _isAudioOn.Value = false;
            }
        }
        if (mustEnable) {
            await _isSpeakerphoneOnStored.WhenRead.ConfigureAwait(false);
            _ = ToggleSpeakerphone(IsSpeakerphoneOn.Value);
        }
        return mustEnable;
    }

    public ValueTask<bool> ToggleSpeakerphone(bool mustEnable)
    {
        lock (_lock) {
            if (IsSpeakerphoneActuallyOn() == mustEnable)
                return ValueTask.FromResult(mustEnable);

            Log.LogDebug("ToggleSpeakerphone({MustEnable})", mustEnable);
            if (mustEnable) {
                var devices = _audioSwitch.AvailableAudioDevices;
                var speakerphone = devices.FirstOrDefault(c => c is AudioDevice.Speakerphone);
                _audioSwitch.SelectDevice(speakerphone);
            }
            else {
                // Force device auto selection, speakerphone has lowest priority,
                // so the headset or earpiece should be selected.
                _audioSwitch.SelectDevice(null);
            }
            TryEnterCommunicationMode();
            _isSpeakerphoneOnStored.Value = IsSpeakerphoneActuallyOn(true);
            IsSpeakerphoneOn.Invalidate();
        }
        return ValueTask.FromResult(mustEnable);
    }

    // Private methods

    private bool IsSpeakerphoneActuallyOn(bool mustLog = false)
        => GetSelectedAudioDevice(mustLog) is AudioDevice.Speakerphone;

    private AudioDevice GetSelectedAudioDevice(bool mustLog = false)
    {
        lock (_lock) {
            var result = _audioSwitch.SelectedAudioDevice;
            if (mustLog)
                Log.LogDebug("GetSelectedAudioDevice() -> {AudioDevice}", result);
            return result;
        }
    }

    private void TryEnterCommunicationMode()
    {
        if (!IsAudioOn.Value)
            return;

        var mode = _audioManager.Mode;
        if (mode == Mode.InCommunication)
            return;

        Log.LogDebug("AudioManager.Mode: {Mode} -> {InCommunication}", mode, Mode.InCommunication);
        _audioManager.Mode = Mode.InCommunication;
    }

    private class StartupCallback : Java.Lang.Object, IFunction2
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? devices, Java.Lang.Object? selectedDevice)
            => null;
    }

    private class FocusChangeListener : Java.Lang.Object, IOnAudioFocusChangeListener
    {
        private ILogger Log { get; }

        public FocusChangeListener(ILogger log)
            => Log = log;

        public void OnAudioFocusChange([GeneratedEnum] AudioFocus focusChange)
            // TODO(DF): handle audio focus change
            => Log.LogDebug("AudioFocus changed: {AudioFocus}", focusChange);
    }

    private class ModeChangedListener : Java.Lang.Object, IOnModeChangedListener
    {
        private ILogger Log { get; }

        public ModeChangedListener(ILogger log)
            => Log = log;

        public void OnModeChanged(int mode)
             => Log.LogDebug("AudioManager.Mode change: {Mode}", (Mode)mode);
    }
}
