namespace ActualChat.Uploads;

public interface IUploadProcessor
{
    bool Supports(string contentType);
    Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken);
}
