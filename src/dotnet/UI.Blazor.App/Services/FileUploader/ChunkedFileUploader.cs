using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChunkedFileUploader(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private IUploads Uploads => Hub.Uploads;

    public async Task UploadData(UploadId uploadId, Task<Stream> getStream, IProgress<double> progressTracker, CancellationToken ct)
    {
        var file = await getStream.ConfigureAwait(false);
        await using (_ = file.ConfigureAwait(false)) {
            var offset = await Uploads.GetOffset(Session, uploadId, ct).ConfigureAwait(false);
            Log.LogDebug("Starting upload of {UploadId} at offset {Offset}", uploadId, offset);

            // Seek to offset if needed
            // TODO(DF): implement what to do if seek is not possible
            if (file.CanSeek && file.Position != offset)
                file.Seek(offset, SeekOrigin.Begin);

            // Upload chunks
            while (offset < file.Length) {
                var remainingBytes = file.Length - offset;
                var currentChunkSize = (int)Math.Min(Constants.Uploads.DefaultChunkSize, remainingBytes);

                var chunkBuffer = new byte[currentChunkSize];
                var bytesRead = await file.ReadAsync(chunkBuffer, 0, currentChunkSize, ct).ConfigureAwait(false);

                if (bytesRead < currentChunkSize) {
                    // Suspicious
                }
                if (bytesRead == 0)
                    break;

                var appendCmd = new Uploads_Append(Session, uploadId, offset, chunkBuffer);
                await UICommander.Run(appendCmd, ct).ConfigureAwait(false);

                offset += bytesRead;
                var uploadProgress = offset / (double)file.Length * 100;
                progressTracker.Report(uploadProgress);
            }
        }
    }
}
