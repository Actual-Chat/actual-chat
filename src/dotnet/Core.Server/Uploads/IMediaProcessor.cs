namespace ActualChat.Uploads;

/// <summary>
/// Processes uploaded files into media content for chat attachments.
/// </summary>
public interface IMediaProcessor
{
    Task<MediaContent> ProcessAttachment(ChatId chatId, UploadedFile uploadedFile, CancellationToken cancellationToken);
}
