namespace ActualChat.Chat.UI.Blazor.Services;

public interface IAudioOutputController
{
    IState<bool> IsAudioOn { get; }
    IState<bool> IsSpeakerphoneOn { get; }
    ValueTask SetAudioEnabled(bool mustEnable);
    ValueTask SetSpeakerphoneEnabled(bool mustEnable);
}

public sealed class AudioOutputController : IAudioOutputController
{
    private readonly IMutableState<bool> _isAudioOn;
    private readonly IMutableState<bool> _isSpeakerphoneOn;

    public IState<bool> IsAudioOn => _isAudioOn;
    public IState<bool> IsSpeakerphoneOn => _isSpeakerphoneOn;

    public AudioOutputController(IServiceProvider services)
    {
        var stateFactory = services.GetRequiredService<IStateFactory>();
        var type = GetType();
        _isAudioOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsAudioOn)));
        _isSpeakerphoneOn = stateFactory.NewMutable(
            false,
            StateCategories.Get(type, nameof(IsSpeakerphoneOn)));
    }

    public ValueTask SetAudioEnabled(bool mustEnable)
    {
        _isAudioOn.Value = mustEnable;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSpeakerphoneEnabled(bool mustEnable)
        => ValueTaskExt.CompletedTask;
}
