namespace ActualChat.Uploads;

public interface IUploadProcessor
{
    bool Supports(string contentType);
    Task<ProcessedFile> Process(UploadedTempFile upload, IProgress<double>? progress, CancellationToken cancellationToken);
}
