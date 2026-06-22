using ActualChat.Flows;
using ActualChat.Media.Flows;
using ActualLab.Rpc;

namespace ActualChat.Media;

/// <summary>
/// Frontend service for managing file uploads with session-based access control.
/// </summary>
public class Uploads(IServiceProvider services) : IUploads
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IUploadsBackend Backend { get; } = services.GetRequiredService<IUploadsBackend>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IMediaProgressBackend MediaProgressBackend { get; } = services.GetRequiredService<IMediaProgressBackend>();
    private ICommander Commander { get; } = services.Commander();
    private FlowHub FlowHub => field ??= services.FlowHub();

    public virtual async Task<long> GetOffset(Session session, UploadId uploadId, CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Backend.GetOffset(uploadId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UploadId> OnCreate(Uploads_Create command, CancellationToken cancellationToken)
    {
        var (session, length, tag, metadata) = command;
        if (length is null)
            throw StandardError.NotSupported("Defer upload length is not supported yet.");
        if (length > Constants.Attachments.FileSizeLimit)
            throw StandardError.Constraint("File is too big.");

        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        EnsureCanCreate(user, length.Value, tag, metadata);
        var uploadId = UploadId.New();
        await Commander.Call(new UploadsBackend_Create(uploadId, user.Id, length, tag, metadata), cancellationToken).ConfigureAwait(false);
        return uploadId;
    }

    // [CommandHandler]
    public virtual async Task OnRemove(Uploads_Remove command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null || upload.UserId != user.Id)
            return;

        await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<long> OnAppend(Uploads_Append command, CancellationToken cancellationToken)
    {
        var (session, uploadId, offset, data) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Commander.Call(new UploadsBackend_Append(uploadId, offset, data), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<long> AppendStream(
        Session session,
        UploadId uploadId,
        long offset,
        RpcStream<byte[]> dataStream,
        CancellationToken cancellationToken)
    {
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).Require().ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);

        const int alignment = Constants.Uploads.StorageBlockAlignment;
        const int flushSize = Constants.Uploads.FlushSize;
        var maxFlushInterval = Constants.Uploads.MaxFlushInterval;

        var currentOffset = offset;
        var buffer = new byte[flushSize];
        var bufferLength = 0;
        var lastFlushAt = Stopwatch.GetTimestamp();
        try {
            await foreach (var subChunk in dataStream.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                var srcOffset = 0;
                while (srcOffset < subChunk.Length) {
                    var toCopy = Math.Min(flushSize - bufferLength, subChunk.Length - srcOffset);
                    Array.Copy(subChunk, srcOffset, buffer, bufferLength, toCopy);
                    bufferLength += toCopy;
                    srcOffset += toCopy;

                    var flushBySize = bufferLength >= flushSize;
                    var flushByTime = bufferLength >= alignment
                        && Stopwatch.GetElapsedTime(lastFlushAt) >= maxFlushInterval;
                    if (flushBySize || flushByTime) {
                        // GCS requires non-final chunks to be 256 KB-aligned, so flush the
                        // largest aligned prefix and keep the remainder buffered.
                        var blockLength = bufferLength - bufferLength % alignment;
                        currentOffset = await Flush(buffer[..blockLength], currentOffset).ConfigureAwait(false);
                        var remaining = bufferLength - blockLength;
                        if (remaining > 0)
                            Array.Copy(buffer, blockLength, buffer, 0, remaining);
                        bufferLength = remaining;
                        lastFlushAt = Stopwatch.GetTimestamp();
                    }
                }
            }
            if (bufferLength > 0)
                currentOffset = await Flush(buffer[..bufferLength], currentOffset).ConfigureAwait(false);
        }
        finally {
            dataStream.Disconnect();
        }
        return currentOffset;

        async Task<long> Flush(byte[] block, long currentOffset1)
            => await Commander.Call(new UploadsBackend_Append(uploadId, currentOffset1, block), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<MediaRef> OnConvertToMediaContent(Uploads_ConvertToMediaRef command, CancellationToken cancellationToken)
    {
        var (session, uploadId) = command;
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);
        return await Commander.Call(new UploadsBackend_ConvertToMediaRef(uploadId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnStartProcessUpload(Uploads_StartProcessUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, uploadId, mediaId) = command;

        // Verify upload ownership
        var user = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccessUpload(upload, user);

        // Verify media ownership
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null)
            throw StandardError.NotFound<Media>();
        if (media.UserId != user.Id)
            throw StandardError.Unauthorized("You don't have permission to access this media.");

        var mediaProgress = await MediaProgressBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (mediaProgress is { Stage: MediaProcessingStage.ServerProcessing }
            && !mediaProgress.Error.IsNullOrEmpty()) {
            // Reset media progress if there was an error reported.
            var progress = new MediaProgress(mediaProgress.Id, 0, MediaProcessingStage.ServerProcessing, mediaProgress.StageProgress);
            await Commander.Run(new MediaProgressBackend_Change(mediaProgress.Id, mediaProgress.Version, Change.Update(progress)), cancellationToken).ConfigureAwait(false);
        }

        // Schedule the UploadProcessingFlow
        await FlowHub
            .NewResumeEvent<UploadProcessingFlow>(UploadProcessingFlow.GetArguments(uploadId, mediaId))
            .Schedule(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureCanAccessUpload([NotNullWhen(true)] Upload? upload, Account user)
    {
        if (upload is null || upload.UserId != user.Id)
            throw StandardError.Upload.NotFound();
    }

    private static void EnsureCanCreate(AccountFull user, long length, string tag, PropertyBag metadata)
    {
        // Add validation by tag and user permissions
    }
}
