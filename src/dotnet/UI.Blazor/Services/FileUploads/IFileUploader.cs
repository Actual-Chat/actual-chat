namespace ActualChat.UI.Blazor.Services;

public interface IFileUploader
{
    bool CanUpload(IUploadStreamSource source);

    Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct);
}
