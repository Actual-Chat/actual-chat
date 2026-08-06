using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class StreamRpcUploader(IServiceProvider services) : IFileUploader
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5d, 3);

    private Session Session => field ??= services.Session();
    private IUploads Uploads => field ??= services.GetRequiredService<IUploads>();
    private ILogger Log => field ??= services.LogFor(GetType());

    public bool CanUpload(IUploadStreamSource source) => source is StreamUploadSource;

    public async Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var streamSource = (StreamUploadSource)source;
        progress ??= new Progress<double>(_ => { });
        var file = await streamSource.GetStream().ConfigureAwait(false);
        await using (file.ConfigureAwait(false)) {
            var retryIndex = 0;
            const int maxRetries = 3;
            while (true) {
                var offset = await Uploads.GetOffset(Session, uploadId, ct).ConfigureAwait(false);
                if (offset > 0) {
                    if (!file.CanSeek)
                        throw StandardError.Internal("Cannot seek in non-seekable stream.");
                    file.Seek(offset, SeekOrigin.Begin);
                }
                if (file.Length > 0)
                    progress.Report(offset / (double)file.Length * 100);
                if (offset >= file.Length)
                    break;

                try {
                    var subChunks = ReadSubChunks(file, offset, file.Length, progress, ct);
                    var dataStream = StandardRpcStream.NewUpload(subChunks);
                    var newOffset = await Uploads.AppendStream(Session, uploadId, offset, dataStream, ct).ConfigureAwait(false);
                    if (newOffset <= offset)
                        throw StandardError.Internal($"Streaming upload made no progress at offset {offset}.");
                    if (newOffset >= file.Length)
                        break;
                }
                catch (Exception e) when (e is UploadTransientException or OffsetConflictException && retryIndex < maxRetries) {
                    Log.LogWarning(e, "Streaming upload failure. Retrying...");
                    retryIndex++;
                    await Task.Delay(RetryDelays.GetDelay(retryIndex), ct).ConfigureAwait(false);
                }
            }
            progress.Report(100);
        }
    }

    private static async IAsyncEnumerable<byte[]> ReadSubChunks(
        Stream file,
        long startOffset,
        long fileLength,
        IProgress<double> progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var position = startOffset;
        while (position < fileLength) {
            var size = (int)Math.Min(Constants.Uploads.SubChunkSize, fileLength - position);
            var buffer = new byte[size];
            var bytesRead = await file.ReadAsync(buffer.AsMemory(0, size), ct).ConfigureAwait(false);
            if (bytesRead == 0)
                break;
            if (bytesRead < size)
                buffer = buffer[..bytesRead];
            position += bytesRead;
            yield return buffer;
            progress.Report(position / (double)fileLength * 100);
        }
    }
}
