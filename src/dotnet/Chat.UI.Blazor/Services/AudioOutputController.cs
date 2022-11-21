namespace ActualChat.Chat.UI.Blazor.Services;

public interface IAudioOutputController : IComputeService
{
    IState<bool> IsSpeakerphoneOn { get; }
    void ToggleAudioDevice(bool enableAudioDevice);
    void SwitchSpeakerphone();
}

public class AudioOutputController : IAudioOutputController
{
    public AudioOutputController(IStateFactory stateFactory)
        => IsSpeakerphoneOn = stateFactory.NewMutable<bool>();

    public IState<bool> IsSpeakerphoneOn { get; }

    public void ToggleAudioDevice(bool enableAudioDevice)
    {
    }

    public void SwitchSpeakerphone()
    {
    }
}
