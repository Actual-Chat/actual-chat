using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class FileUploader(IEnumerable<IFileUploader> uploaders)
{
    private IReadOnlyList<IFileUploader> Uploaders { get; } = uploaders.ToList();

    public Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var uploader = Uploaders.FirstOrDefault(x => x.CanUpload(source))
            ?? throw StandardError.Internal($"No uploader for {source.GetType().Name}");
        return uploader.Upload(source, uploadId, progress, ct);
    }
}
