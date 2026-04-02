namespace ActualChat.Uploads;

/// <summary>
/// Processes uploaded files into media content for chat attachments.
/// </summary>
public interface IMediaProcessor
{
    Task<ProcessedFile> ProcessUpload(
        UploadedFile uploadedFile,
        MediaKind mediaKind,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
