using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class StreamUploader(ChunkedFileUploader chunkedUploader) : IFileUploader
{
    public bool CanUpload(IUploadSource source) => source is StreamUploadSource;

    public async Task Upload(
        IUploadSource source,
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
