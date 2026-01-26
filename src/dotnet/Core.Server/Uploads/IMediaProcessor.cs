namespace ActualChat.Uploads;

/// <summary>
/// Processes uploaded files into media content for chat attachments.
/// </summary>
public interface IMediaProcessor
{
    Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, IProgress<double>? progress, CancellationToken cancellationToken);
    Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, CancellationToken cancellationToken)
        => ProcessUpload(uploadedFile, null, cancellationToken);
}
