using System.Runtime.ExceptionServices;
using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChunkedFileUploader(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private IUploads Uploads => Hub.Uploads;

    public async Task<Result<Unit>> UploadData(
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
                var uploadResult = await UploadDataInternal(
                    uploadId,
                    file,
                    progressTracker,
                    () => retryIndex = 0,
                    ct).ConfigureAwait(false);
                switch (uploadResult.Error) {
                    case NotFoundException<Upload> e1:
                        return Result.NewError<Unit>(e1);
                    case OffsetConflictException e2 when retryIndex <= maxRetries:
                        Log.LogWarning(e2, "Offset conflict detected. Retrying...");
                        retryIndex++;
                        run = true;
                        continue;
                }
                if (uploadResult.Error is not null)
                    ExceptionDispatchInfo.Capture(uploadResult.Error).Throw();
            }
        }
        return Result.New(Unit.Default);
    }

    private async Task<Result<Unit>> UploadDataInternal(
        UploadId uploadId,
        Stream file,
        IProgress<double> progressTracker,
        Action onChunkUploadSucceeded,
        CancellationToken ct)
    {
        var offsetResult = await Uploads.GetOffset(Session, uploadId, ct).ConfigureAwait(false);
        if (offsetResult.Error is not null)
            return Result.NewError<Unit>(offsetResult.Error);

        var offset = offsetResult.Value;
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
            var appendResult = await Commander.Call(appendCmd, ct).ConfigureAwait(false);
            if (appendResult.Error is not null)
                return Result.NewError<Unit>(appendResult.Error);

            var newOffset = appendResult.Value;
            offset += bytesRead;
            if (offset != newOffset)
                Log.LogWarning("Offset mismatch detected: {Offset} != {NewOffset}", offset, newOffset);
            onChunkUploadSucceeded();
            var uploadProgress = offset / (double)file.Length * 100;
            progressTracker.Report(uploadProgress);
        }

        return Result.New(Unit.Default);
    }
}
