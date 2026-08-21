using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class UploadOperations(AppUIHub hub)
{
    private static readonly TimeSpan MonitorServerProcessingTimeout = TimeSpan.FromMinutes(15);

    private readonly FileUploadQueue _uploadQueue = new ();
    private readonly FileUploader _uploader
        = hub.Services.GetRequiredService<FileUploader>();

    public Session Session => hub.Session;
    public ICommander Commander => hub.Commander;
    public Moment Now() => hub.Clocks.SystemClock.Now;
    public IMedia Media => hub.Media;
    public VideoTranscoder VideoTranscoder => field ??= hub.VideoTranscoder;
    private ILogger Log => field ??= hub.LogFor(GetType());

    public async Task<MediaId> ReserveMediaId(
        UploadSessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var sessionMetadata = snapshot.Metadata;
        var fileMetadata = snapshot.FileProvider.Metadata;
        var metadata = sessionMetadata
            .Set(nameof(ActualChat.Media.Media.FileName), fileMetadata.FileName)
            .Set(nameof(ActualChat.Media.Media.ContentType), fileMetadata.FileType)
            .Set(nameof(ActualChat.Media.Media.Length), fileMetadata.Length);
        var mediaScope = snapshot.MediaScope.NullIfEmpty() ?? MediaId.NewScope();
        var command = new Media_ReserveMedia { Session = Session, Scope = mediaScope, Metadata = metadata };
        var mediaId = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return mediaId;
    }

    public async Task UploadData(
        UploadSource source,
        UploadSessionSnapshotAccessor snapshotAccessor,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = snapshotAccessor.Get();
        var fileProvider = snapshot.FileProvider;
        await fileProvider.WhenFileStreamReady().WaitAsync(cancellationToken).ConfigureAwait(false);
        await GetOrRegisterUpload(source, snapshotAccessor, cancellationToken).ConfigureAwait(false); // Ensure upload id is registered
        progress ??= new Progress<double>(_ => { });
        var uploadOperation = new FileUploadOperation(StartUpload);
        _uploadQueue.Enqueue(uploadOperation);
        await uploadOperation.Task.ConfigureAwait(false);
        return;

        async Task StartUpload()
        {
            try {
                var uploadId = await GetOrRegisterUpload(source, snapshotAccessor, cancellationToken).ConfigureAwait(false);
                await _uploader.Upload(source.StreamSource, uploadId, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (UploadNotFoundException) {
                await snapshotAccessor.Update(s => s with { UploadId = null }, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task StartServerProcessing(UploadSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var cmd = new Uploads_StartProcessUpload {
            Session = Session,
            UploadId = snapshot.UploadId.Require(),
            MediaId = snapshot.ReservedMediaId.Require(),
        };
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaRef> WaitForProcessingCompletion(
        UploadSessionSnapshot snapshot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken1 = default)
    {
        var cts = new CancellationTokenSource(MonitorServerProcessingTimeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken1);
        var ct2 = linkedCts.Token;
        try {
            var mediaId = snapshot.ReservedMediaId.Require();
            var cStatus = await Computed
                .Capture(() => Media.GetProgress(Session, mediaId, ct2), ct2)
                .ConfigureAwait(false);

            var changes = cStatus.Changes(FixedDelayer.NoneUnsafe, ct2);
            await foreach (var c in changes.ConfigureAwait(false)) {
                var (status, error) = c;
                if (error != null) {
                    Log.LogWarning(error, "Error getting status for media {MediaId}", mediaId);
                    continue;
                }
                if (status == null)
                    throw new InvalidOperationException("Media not found");

                if (status.HasFailed)
                    throw new InvalidOperationException(status.Error);

                if (status.Stage is MediaProcessingStage.Ready) {
                    var content = await Media.GetContent(Session, mediaId, ct2).ConfigureAwait(false);
                    if (content == null)
                        throw new InvalidOperationException("Media content not found after Ready status");

                    return content;
                }
                if (status.Stage is MediaProcessingStage.ServerProcessing)
                    progress?.Report(status.StageProgress);
            }
        }
        catch (OperationCanceledException) when (cancellationToken1.IsCancellationRequested) {
            throw;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) {
            throw new TimeoutException("Monitoring processing timed out");
        }

        throw StandardError.Internal("Unexpected state");
    }

    public async Task RemoveUpload(UploadId uploadId, CancellationToken cancellationToken)
        => await Commander.Call(new Uploads_Remove {
            Session = Session,
            UploadId = uploadId,
        }, cancellationToken).ConfigureAwait(false);

    private async Task<UploadId> GetOrRegisterUpload(
        UploadSource source,
        UploadSessionSnapshotAccessor snapshotAccessor,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshotAccessor.Get();
        var uploadId = snapshot.UploadId;
        if (uploadId is not null)
            return uploadId;

        uploadId = await RegisterUploadId(source.Metadata, cancellationToken).ConfigureAwait(false);
        await snapshotAccessor.Update(s => s with { UploadId = uploadId }, cancellationToken).ConfigureAwait(false);
        return uploadId;
    }

    private async Task<UploadId> RegisterUploadId(UploadSourceMetadata sourceMetadata, CancellationToken cancellationToken)
    {
        var length = sourceMetadata.Length;
        var metadata = new MetadataBag()
            .Set(nameof(ActualChat.Media.Media.FileName), sourceMetadata.FileName.Value)
            .Set(nameof(ActualChat.Media.Media.ContentType), sourceMetadata.ContentType);
        return await Commander.Call(new Uploads_Create {
            Session = Session,
            Length = length,
            Tag = "",
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
    }
}
