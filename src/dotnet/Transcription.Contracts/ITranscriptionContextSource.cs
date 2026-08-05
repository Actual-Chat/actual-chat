namespace ActualChat.Transcription;

/// <summary>
/// Builds the chat-derived context handed to transcribers that accept one.
/// </summary>
public interface ITranscriptionContextSource
{
    Task<TranscriptionContext> GetContext(ChatId chatId, CancellationToken cancellationToken);
}
