namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatAudioState(
    ChatId? ChatId,
    bool IsListening = false,
    bool IsReplaying = false,
    bool IsRecording = false)
{
    public static readonly ChatAudioState None = new ((ChatId?)null);
}
