using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Media.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class UploadProcessingFlow : Flow<MediaContent>
{
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IUploadsBackend UploadsBackend => field ??= Services.GetRequiredService<IUploadsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    public static string GetArguments(UploadId uploadId, MediaId mediaId)
        => FlowId.CombineArguments(uploadId.Value, mediaId.Value);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var (uploadId, mediaId) = ParseArgs();

        // Verify media exists and has no content yet
        var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null) {
            SetError(StandardError.NotFound<Media>());
            return;
        }
        if (!media.ContentId.IsNullOrEmpty()) {
            Console.Log("Media already has content");
            SetResult(new Result<MediaContent>(new MediaContent(mediaId, media.ContentId)));
            return;
        }

        // Verify upload exists
        var upload = await UploadsBackend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload == null) {
            SetError(new InvalidOperationException("Upload not found"));
            return;
        }

        try {
            // // Update status to ServerProcessing
            // var statusInfo = new MediaStatusInfo(mediaId, MediaStatus.Preparing, MediaPreparingStage.ServerProcessing);
            // var statusChange = new Change<MediaStatusInfo> { Update = statusInfo };
            // await Commander.Call(new MediaStatusBackend_Change(mediaId, statusChange), cancellationToken).ConfigureAwait(false);

            // Process upload and bind to media
            var mediaContent = await Commander.Call(new UploadsBackend_ProcessAndSaveContent(uploadId, mediaId), cancellationToken).ConfigureAwait(false);
            Console.Log("Upload processed and saved");

            // // Remove the upload
            // await Commander.Call(new UploadsBackend_Remove(uploadId), cancellationToken).ConfigureAwait(false);
            // Console.Log("Upload removed");

            SetResult(mediaContent);
        }
        catch (Exception e) {
            // Set status to Failed
            Console.Log($"Processing failed: {e.Message}");
            var failedStatus = new MediaStatusInfo(mediaId, MediaStatus.Failed);
            var failedChange = new Change<MediaStatusInfo> { Update = failedStatus };
            await Commander.Call(new MediaStatusBackend_Change(mediaId, failedChange), cancellationToken).ConfigureAwait(false);
            SetError(e);
        }
    }

    private (UploadId uploadId, MediaId mediaId) ParseArgs()
    {
        var args = Id.SplitArguments();
        var uploadId = UploadId.Parse(args[0]);
        var mediaId = MediaId.Parse(args[1]);
        return (uploadId, mediaId);
    }
}
