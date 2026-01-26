namespace ActualChat.Uploads;

/// <summary>
/// Processes uploaded files (e.g., image resizing, validation).
/// </summary>
public interface IUploadProcessor
{
    bool Supports(string contentType);
    Task<ProcessedFile> Process(UploadedTempFile upload, IProgress<double>? progress, CancellationToken cancellationToken);
}
