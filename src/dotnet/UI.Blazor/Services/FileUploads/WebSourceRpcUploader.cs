using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebSourceRpcUploader(ChunkedFileUploader chunkedUploader) : IFileUploader
{
    public bool CanUpload(IUploadStreamSource source) => source is WebUploadStreamSource;

    public async Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var webSource = (WebUploadStreamSource)source;
        progress ??= new Progress<double>(_ => { });

        var size = await webSource.JSRef
            .InvokeAsync<long>("getBlobSize", ct)
            .ConfigureAwait(false);
        var stream = (Stream)new JSBlobStream(webSource.JSRef, size);
        await chunkedUploader.UploadData(uploadId, Task.FromResult(stream), progress, ct).ConfigureAwait(false);
    }
}
