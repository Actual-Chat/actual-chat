namespace ActualChat.Audio.UI.Blazor.Services;

public interface IAudioDeviceController
{
    IState<bool> IsPlaybackOn { get; }
    IState<bool> IsRecordingOn { get; }
    IState<bool> IsSpeakerphoneOn { get; }
    ValueTask SetPlaybackEnabled(bool mustEnable);
    ValueTask SetRecordingEnabled(bool mustEnable);
    ValueTask SetSpeakerphoneEnabled(bool mustEnable);
}

public sealed class AudioDeviceController : IAudioDeviceController
{
    private readonly IMutableState<bool> _isPlaybackOn;
    private readonly IMutableState<bool> _isRecordingOn;
    private readonly IMutableState<bool> _isSpeakerphoneOn;

    public IState<bool> IsPlaybackOn => _isPlaybackOn;
    public IState<bool> IsRecordingOn => _isRecordingOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;

    public AudioDeviceController(IServiceProvider services)
    {
        var stateFactory = services.GetRequiredService<IStateFactory>();
        var type = GetType();
        _isPlaybackOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsPlaybackOn)));
        _isRecordingOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsRecordingOn)));
        _isSpeakerphoneOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsSpeakerphoneOn)));
    }

    public ValueTask SetPlaybackEnabled(bool mustEnable)
    {
        _isPlaybackOn.Value = mustEnable;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRecordingEnabled(bool mustEnable)
        => ValueTask.CompletedTask;

    public ValueTask SetSpeakerphoneEnabled(bool mustEnable)
        => ValueTask.CompletedTask;
}
