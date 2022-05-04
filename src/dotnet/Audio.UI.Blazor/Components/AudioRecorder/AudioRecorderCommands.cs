namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderCommand
{ }

public sealed record StartAudioRecorderCommand(Symbol ChatId) : IAudioRecorderCommand;

public sealed class StopAudioRecorderCommand : IAudioRecorderCommand
{
    public static StopAudioRecorderCommand Instance { get; } = new();
}
