using ActualChat.Flows;

namespace ActualChat.Media.Flows;

[Flow(ResumeTimeout = 14.5 * 60)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial class UploadProcessingFlow : Flow<MediaContent>
{
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IUploadsBackend UploadsBackend => field ??= Services.GetRequiredService<IUploadsBackend>();
    private ICommander Commander => field ??= Services.Commander();
    private IMediaProgressBackend MediaProgressBackend => field ??= Services.GetRequiredService<IMediaProgressBackend>();

    public static string GetArguments(UploadId uploadId, MediaId mediaId)
        => FlowId.CombineArguments(uploadId.Value, mediaId.Value);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        var (uploadId, mediaId) = ParseArgs();
        try {
            // Verify media exists and has no content yet
            var media = await MediaBackend.GetFull(mediaId, cancellationToken).ConfigureAwait(false);
            if (media == null)
                throw StandardError.NotFound<Media>();

            if (!media.BlobId.IsNullOrEmpty()) {
                Console.Log("Media already has content");
                SetResult(new MediaContent(mediaId, media.BlobId));
                return;
            }

            // Verify upload exists
            var upload = await UploadsBackend.Get(uploadId, cancellationToken).ConfigureAwait(false);
            if (upload == null)
                throw StandardError.NotFound<Upload>();

            try {
                // Process upload and bind to media
                var mediaContent = await Commander
                    .Call(new UploadsBackend_ProcessAndSaveContent(uploadId, mediaId), cancellationToken)
                    .ConfigureAwait(false);
                Console.Log("Upload processed and saved");
                SetResult(mediaContent);
            }
            catch (Exception e) {
                Console.Log($"Processing failed: {e.Message}");
                throw;
            }
        }
        catch {
            await ActualChat.Media.UploadsBackend.ReportMediaServerProcessingError(
                MediaProgressBackend,
                Commander,
                mediaId,
                "",
                cancellationToken).ConfigureAwait(false);
            throw;
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
