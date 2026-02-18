namespace ActualChat.UI.Blazor.App.Services;

public interface IFileUploader
{
    bool CanUpload(IUploadSource source);

    Task Upload(
        IUploadSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct);
}
