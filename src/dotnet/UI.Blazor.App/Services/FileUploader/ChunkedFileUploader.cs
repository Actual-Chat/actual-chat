using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChunkedFileUploader(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private static readonly RetryDelaySeq RetryDelays = RetryDelaySeq.Exp(0.5d, 3);
    private IUploads Uploads => Hub.Uploads;

    public async Task UploadData(
        UploadId uploadId,
        Task<Stream> getStream,
        IProgress<double> progressTracker,
        CancellationToken ct)
    {
        var file = await getStream.ConfigureAwait(false);
        var retryIndex = 0;
        const int maxRetries = 3;
        await using (_ = file.ConfigureAwait(false)) {
            bool run = true;
            while (run) {
                run = false;
                try {
                    await UploadDataInternal(
                            uploadId,
                            file,
                            progressTracker,
                            () => retryIndex = 0,
                            ct)
                        .ConfigureAwait(false);
                }
                catch (UploadTransientException e) when (retryIndex <= maxRetries) {
                    Log.LogWarning(e, "Upload transient failure. Retrying...");
                    retryIndex++;
                    await Task.Delay(RetryDelays.GetDelay(retryIndex), ct).ConfigureAwait(false);
                    run = true;
                }
                catch (OffsetConflictException e) when (retryIndex <= maxRetries) {
                    Log.LogWarning(e, "Offset conflict detected. Retrying...");
                    retryIndex++;
                    run = true;
                }
            }
        }
    }

    private async Task UploadDataInternal(
        UploadId uploadId,
        Stream file,
        IProgress<double> progressTracker,
        Action onChunkUploadSucceeded,
        CancellationToken ct)
    {
        var offset = await Uploads.GetOffset(Session, uploadId, ct).ConfigureAwait(false);
        Log.LogDebug("Starting upload of {UploadId} at offset {Offset}", uploadId, offset);

        if (offset > 0) {
            if (file.CanSeek)
                file.Seek(offset, SeekOrigin.Begin);
            else
                throw StandardError.Internal("Cannot seek in non-seekable stream.");
        }

        // Upload chunks
        while (offset < file.Length) {
            var remainingBytes = file.Length - offset;
            var currentChunkSize = (int)Math.Min(Constants.Uploads.DefaultChunkSize, remainingBytes);

            var chunkBuffer = new byte[currentChunkSize];
            var bytesRead = await file.ReadAsync(chunkBuffer, 0, currentChunkSize, ct).ConfigureAwait(false);

            if (bytesRead < currentChunkSize)
                Log.LogWarning("Unexpected EOF while reading chunk {Offset}. Expected to read {ChunkSize} bytes, but read only {ReadBytes} bytes",
                    offset, currentChunkSize, bytesRead);
            if (bytesRead == 0)
                break;

            var appendCmd = new Uploads_Append(Session, uploadId, offset, chunkBuffer);
            var newOffset = await Commander.Call(appendCmd, ct).ConfigureAwait(false);
            offset += bytesRead;
            if (offset != newOffset)
                Log.LogWarning("Offset mismatch detected: {Offset} != {NewOffset}", offset, newOffset);
            onChunkUploadSucceeded();
            var uploadProgress = offset / (double)file.Length * 100;
            progressTracker.Report(uploadProgress);
        }
    }
}
