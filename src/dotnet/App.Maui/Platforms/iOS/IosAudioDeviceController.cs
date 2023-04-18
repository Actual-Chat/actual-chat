using ActualChat.Audio.UI.Blazor.Services;
using AVFoundation;

namespace ActualChat.App.Maui;

public class IosAudioDeviceController : IAudioDeviceController
{
    private readonly object _lock = new ();
    private readonly IMutableState<bool> _isPlaybackOn;
    private readonly IMutableState<bool> _isRecordingOn;
    private readonly IMutableState<bool> _isSpeakerphoneOn;

    private ILogger Log { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsPlaybackOn => _isPlaybackOn;
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsRecordingOn => _isRecordingOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;


    public IosAudioDeviceController(IServiceProvider services)
    {
        var type = GetType();
        var stateFactory = services.GetRequiredService<IStateFactory>();
        Log = services.LogFor(type);
        _isPlaybackOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsPlaybackOn)));
        _isRecordingOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsPlaybackOn)));
        _isSpeakerphoneOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsSpeakerphoneOn)));
    }

    public ValueTask SetPlaybackEnabled(bool mustEnable)
    {
        lock (_lock) {
            if (_isPlaybackOn.Value == mustEnable)
                return ValueTask.CompletedTask;

            _isPlaybackOn.Value = mustEnable;
            ConfigureAudioSessionUnsafe();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetRecordingEnabled(bool mustEnable)
    {
        lock (_lock) {
            if (_isRecordingOn.Value == mustEnable)
                return ValueTask.CompletedTask;

            _isRecordingOn.Value = mustEnable;
            ConfigureAudioSessionUnsafe();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetSpeakerphoneEnabled(bool mustEnable)
        => ValueTaskExt.CompletedTask;

    private void ConfigureAudioSessionUnsafe()
    {
        var isPlaybackOn = _isPlaybackOn.Value;
        var isRecordingOn = _isRecordingOn.Value;
        var isAudioOn = isPlaybackOn || isRecordingOn;
        try {
            var avAudioSession = AVAudioSession.SharedInstance();
            if (isAudioOn) {
                avAudioSession.SetCategory(GetCategory(), GetCategoryOptions()).Assert();
                if (!avAudioSession.OverrideOutputAudioPort(AVAudioSessionPortOverride.Speaker, out var error))
                    error.Assert();
            }

            avAudioSession.SetActive(isAudioOn, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation).Assert();
        }
        catch (Exception e) {
            DefaultLog.LogError(e, "Failed to configure audio session");
        }

        AVAudioSessionCategory GetCategory()
        {
            if (isRecordingOn && isPlaybackOn)
                return AVAudioSessionCategory.PlayAndRecord;

            if (isPlaybackOn)
                return AVAudioSessionCategory.Playback;

            return AVAudioSessionCategory.Record;
        }

        AVAudioSessionCategoryOptions GetCategoryOptions()
        {
            var options = AVAudioSessionCategoryOptions.AllowBluetooth
                // | AVAudioSessionCategoryOptions.AllowBluetoothA2DP TODO: only for audio playback
                | AVAudioSessionCategoryOptions.DuckOthers
                | AVAudioSessionCategoryOptions.DefaultToSpeaker;

            if (!isRecordingOn)
                options |= AVAudioSessionCategoryOptions.AllowBluetoothA2DP;

            return options;
        }
    }
}
