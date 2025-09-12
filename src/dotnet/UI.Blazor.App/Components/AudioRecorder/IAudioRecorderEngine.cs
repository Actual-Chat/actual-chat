namespace ActualChat.UI.Blazor.App.Components;

public interface IAudioRecorderEngine
{
    Task<bool> StartAsync(ChatId chatId, ChatEntryId? repliedChatEntryId, string sessionToken, CancellationToken cancellationToken = default);
    Task<bool> StopAsync(CancellationToken cancellationToken = default);
    ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken);
    ValueTask ConversationSignal(CancellationToken cancellationToken);
    Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken);
}
