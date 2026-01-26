namespace ActualChat.Uploads;

public interface IMediaProcessor
{
    Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, IProgress<double>? progress, CancellationToken cancellationToken);
    Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, CancellationToken cancellationToken)
        => ProcessUpload(uploadedFile, null, cancellationToken);
}
