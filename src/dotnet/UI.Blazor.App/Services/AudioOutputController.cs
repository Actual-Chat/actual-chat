namespace ActualChat.UI.Blazor.App.Services;

public interface IAudioOutputController
{
    IState<bool> IsAudioOn { get; }
    IState<bool> IsSpeakerphoneOn { get; }
    ValueTask<bool> ToggleAudio(bool mustEnable);
    ValueTask<bool> ToggleSpeakerphone(bool mustEnable);
}

public sealed class AudioOutputController : ScopedServiceBase<UIHub>, IAudioOutputController
{
    private readonly MutableState<bool> _isAudioOn;
    private readonly MutableState<bool> _isSpeakerphoneOn;

    public IState<bool> IsAudioOn => _isAudioOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;

    public AudioOutputController(UIHub hub) : base(hub)
    {
        var stateFactory = StateFactory;
        var type = GetType();
        _isAudioOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsAudioOn)));
        _isSpeakerphoneOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsSpeakerphoneOn)));
    }

    public ValueTask<bool> ToggleAudio(bool mustEnable)
        => ValueTaskExt.FromResult(_isAudioOn.Value = mustEnable);

    public ValueTask<bool> ToggleSpeakerphone(bool mustEnable)
        => ValueTaskExt.FalseTask;
}
