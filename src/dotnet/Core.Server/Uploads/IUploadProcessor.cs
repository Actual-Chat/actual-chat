namespace ActualChat.Uploads;

public interface IUploadProcessor
{
    bool Supports(string contentType);
    Task<ProcessedFile> Process(UploadedTempFile upload, CancellationToken cancellationToken);
}
