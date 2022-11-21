using ActualChat.Chat.UI.Blazor.Services;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Systems;
using Android.Util;
using Kotlin.Jvm.Functions;
using Xamarin.Twilio.AudioSwitch;
using static Android.Media.AudioManager;

namespace ActualChat.App.Maui;

internal class AndroidAudioOutputController : IAudioOutputController
{
    private readonly TaskSource<Unit> _whenInitializedSource;
    private AudioSwitch? _audioSwitch;
    private AudioManager? _audioManager;

    public AndroidAudioOutputController(IStateFactory stateFactory)
    {
        _whenInitializedSource = TaskSource.New<Unit>(true);
        var whenInitialized = _whenInitializedSource.Task;
        IsSpeakerphoneOn = stateFactory.NewComputed<bool>(async (_, _) => {
            await whenInitialized.ConfigureAwait(false);
            return IsSpeakerphoneOnInternal();
        });
    }

    public IState<bool> IsSpeakerphoneOn { get; }

    public void ToggleAudioDevice(bool enableAudioDevice)
    {
        EnsureInitialized();
        if (enableAudioDevice)
            _audioSwitch!.Activate();
        else
            _audioSwitch!.Deactivate();
    }

    public void SwitchSpeakerphone()
    {
        EnsureInitialized();
        Log.Debug(AndroidConstants.LogTag, "About to switch Speakerphone");
        var devices = _audioSwitch!.AvailableAudioDevices;
        var selectedDevice = _audioSwitch.SelectedAudioDevice;
        Log.Debug(AndroidConstants.LogTag, $"Selected audio device: {selectedDevice}");
        if (selectedDevice is null)
            // if no selected device, force device auto selection
            _audioSwitch.SelectDevice(null);
        else if (selectedDevice is AudioDevice.Speakerphone)
            // force device auto selection, speakerphone has lowest priority, so headset or earpiece should be selected
            _audioSwitch.SelectDevice(null);
        else {
            var speakerphone = devices.FirstOrDefault(c => c is AudioDevice.Speakerphone);
            _audioSwitch.SelectDevice(speakerphone);
        }

        Log.Debug(AndroidConstants.LogTag, $"AudioManager.Mode: {_audioManager!.Mode}");
        if (_audioManager.Mode != Mode.InCommunication) {
            // TODO(DF): I can't understand how it happens that mode is changed to Normal when I switch speakerphoneOn multiple times fast.
            // Switching to Normal mode breaks reaction on speakerphoneOn changes.
        }
        InvalidateSelectedDeviceComputed();
    }

    private void EnsureInitialized()
    {
        if (_audioSwitch != null)
            return;

        _audioSwitch = new AudioSwitch(Platform.AppContext, true, new FocusChangeListener());
        _audioSwitch.Start(new StartupCallback());
        _audioManager = Platform.AppContext.GetSystemService(Context.AudioService) as AudioManager;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S) {
            try {
                _audioManager!.AddOnModeChangedListener(Platform.AppContext.MainExecutor!, new ModeChangedListener());
            }
            catch(Exception e) {
                Log.Warn(AndroidConstants.LogTag, Java.Lang.Throwable.FromException(e), "Failed to add ModeChangedListener");
            }
        }
        _whenInitializedSource.TrySetResult(default);
    }

    private bool IsSpeakerphoneOnInternal()
    {
        EnsureInitialized();
        return _audioSwitch!.SelectedAudioDevice is AudioDevice.Speakerphone;
    }

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

    private class FocusChangeListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
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
