namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderEngine
{
    Task<RecorderStartOutcome> Start(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        CancellationToken cancellationToken = default);
    Task<bool> Stop(CancellationToken cancellationToken = default);
    ValueTask ConversationSignal(CancellationToken cancellationToken);
    Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken);
}
