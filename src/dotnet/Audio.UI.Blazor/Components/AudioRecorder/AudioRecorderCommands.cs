namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderCommand
{ }

public sealed record StartAudioRecorderCommand(ChatId ChatId, CancellationTokenSource RecordingCancellation) : IAudioRecorderCommand;

public sealed class StopAudioRecorderCommand : IAudioRecorderCommand
{
    public static StopAudioRecorderCommand Instance { get; } = new();
}
