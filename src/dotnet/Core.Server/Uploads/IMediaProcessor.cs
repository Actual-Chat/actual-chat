namespace ActualChat.Uploads;

public interface IMediaProcessor
{
    Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, CancellationToken cancellationToken);
}
