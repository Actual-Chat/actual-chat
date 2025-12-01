using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChunkedFileUploader(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private IUploads Uploads => Hub.Uploads;

    [RequiresUnreferencedCode("Uses ReadFromJsonAsync")]
    public FileUploadOperation CreateUploadOperation(UploadId uploadId, Stream file)
    {
        var progress = new UploadProgressTracker();
        return new FileUploadOperation(async token => {
            var offset = await Uploads.GetOffset(Session, uploadId, token).ConfigureAwait(false);
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
                var bytesRead = await file.ReadAsync(chunkBuffer, 0, currentChunkSize, token).ConfigureAwait(false);

                if (bytesRead < currentChunkSize) {
                    // Suspicious
                }
                if (bytesRead == 0)
                    break;

                var appendCmd = new Uploads_Append(Session, uploadId, chunkBuffer, offset);
                await UICommander.Run(appendCmd, token).ConfigureAwait(false);

                offset += bytesRead;
                var uploadProgress = offset / (double)file.Length * 100;
                progress.ReportProgress(uploadProgress);
            }

            // Convert uploaded file to media content
            var mediaContent = await UICommander.Call(new Uploads_Complete(Session, uploadId), token).ConfigureAwait(false);
            return mediaContent;
        }, progress);
    }
}
