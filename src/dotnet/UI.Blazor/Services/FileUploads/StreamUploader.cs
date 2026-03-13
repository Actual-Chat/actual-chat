using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class StreamUploader(ChunkedFileUploader chunkedUploader) : IFileUploader
{
    public bool CanUpload(IUploadStreamSource source) => source is StreamUploadSource;

    public async Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var streamSource = (StreamUploadSource)source;
        progress ??= new Progress<double>(_ => { });
        await chunkedUploader.UploadData(
            uploadId,
            streamSource.GetStream(),
            progress,
            ct).ConfigureAwait(false);
    }
}
