namespace ActualChat.UI.Blazor.App.Services;

public class WebSourceUploader : IFileUploader
{
    public bool CanUpload(IUploadSource source) => source is WebUploadSource;

    public async Task Upload(
        IUploadSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var webSource = (WebUploadSource)source;
        progress ??= new Progress<double>(_ => { });
        await webSource.WebFileProviderInternal
            .UploadData(uploadId, progress, ct)
            .ConfigureAwait(false);
    }
}
