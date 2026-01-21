namespace ActualChat.Uploads;

public interface IMediaProcessor
{
    Task<MediaContent> ProcessAttachment(ChatId chatId, UploadedFile uploadedFile, CancellationToken cancellationToken);
}
