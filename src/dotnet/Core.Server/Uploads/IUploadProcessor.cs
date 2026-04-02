namespace ActualChat.Uploads;

/// <summary>
/// Processes uploaded files (e.g., image resizing, validation).
/// </summary>
public interface IUploadProcessor
{
    bool Supports(string contentType, MediaKind mediaKind);
    Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken);
}
