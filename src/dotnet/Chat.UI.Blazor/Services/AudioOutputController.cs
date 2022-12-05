namespace ActualChat.Chat.UI.Blazor.Services;

public interface IAudioOutputController
{
    IState<bool> IsAudioOn { get; }
    IState<bool> IsSpeakerphoneOn { get; }
    ValueTask<bool> ToggleAudio(bool mustEnable);
    ValueTask<bool> ToggleSpeakerphone(bool mustEnable);
}

public sealed class AudioOutputController : IAudioOutputController
{
    private readonly IMutableState<bool> _isAudioOn;
    private readonly IMutableState<bool> _isSpeakerphoneOn;

    public AudioOutputController(IStateFactory stateFactory)
    {
        _isAudioOn = stateFactory.NewMutable<bool>();
        _isSpeakerphoneOn = stateFactory.NewMutable<bool>();
    }

    public IState<bool> IsAudioOn => _isAudioOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;

    public ValueTask<bool> ToggleAudio(bool mustEnable)
        => ValueTaskExt.FromResult(_isAudioOn.Value = mustEnable);

    public ValueTask<bool> ToggleSpeakerphone(bool mustEnable)
        => ValueTaskExt.FalseTask;
}
