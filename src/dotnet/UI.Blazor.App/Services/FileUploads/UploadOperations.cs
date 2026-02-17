using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public class UploadOperations(AppUIHub hub)
{
    private readonly FileUploadQueue _uploadQueue = new ();

    private static readonly TimeSpan MonitorServerProcessingTimeout = TimeSpan.FromMinutes(15);

    public Session Session => hub.Session;
    public ICommander Commander => hub.Commander;
    public Moment Now() => hub.Clocks.SystemClock.Now;
    public IMedias Medias => hub.Medias;
    private ILogger Log => field ??= hub.LogFor(GetType());

    public async Task<MediaId> ReserveMediaId(
        UploadSessionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var sessionMetadata = snapshot.Metadata;
        var fileMetadata = snapshot.FileProvider.Metadata;
        var metadata = sessionMetadata
            .Set(nameof(Media.Media.FileName), fileMetadata.FileName)
            .Set(nameof(Media.Media.ContentType), fileMetadata.FileType)
            .Set(nameof(Media.Media.Length), fileMetadata.Length);
        var mediaScope = snapshot.MediaScope.NullIfEmpty() ?? MediaId.NewScope();
        var command = new Medias_ReserveMedia(Session, mediaScope) { Metadata = metadata };
        var mediaId = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return mediaId;
    }

    public async Task UploadData(
        UploadSessionSnapshotAccessor snapshotAccessor,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = snapshotAccessor.Get();
        var fileProvider = snapshot.FileProvider;
        await fileProvider.WhenFileStreamReady().WaitAsync(cancellationToken).ConfigureAwait(false);
        await GetOrRegisterUpload(snapshotAccessor, cancellationToken).ConfigureAwait(false); // Ensure upload id is registered
        progress ??= new Progress<double>(_ => { });
        var uploadOperation = new FileUploadOperation(StartUpload);
        _uploadQueue.Enqueue(uploadOperation);
        await uploadOperation.Task.ConfigureAwait(false);
        return;

        async Task StartUpload()
        {
            try {
                var uploadId = await GetOrRegisterUpload(snapshotAccessor, cancellationToken).ConfigureAwait(false);
                await fileProvider.UploadData(uploadId, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (UploadNotFoundException) {
                await snapshotAccessor.Update(s => s with { UploadId = null }, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task StartServerProcessing(UploadSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var cmd = new Uploads_StartProcessUpload(
            Session,
            snapshot.UploadId.Require(),
            snapshot.ReservedMediaId.Require());
        await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaContent> WaitForProcessingCompletion(
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
                .Capture(() => Medias.GetProgress(Session, mediaId, ct2), ct2)
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
                    throw new InvalidOperationException(status.ErrorMessage);

                if (status.Stage is MediaStage.Ready) {
                    var content = await Medias.GetContent(Session, mediaId, ct2).ConfigureAwait(false);
                    if (content == null)
                        throw new InvalidOperationException("Media content not found after Ready status");

                    return content;
                }
                if (status.Stage is MediaStage.ServerProcessing)
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
        => await Commander.Call(new Uploads_Remove(Session, uploadId), cancellationToken).ConfigureAwait(false);

    private async Task<UploadId> GetOrRegisterUpload(
        UploadSessionSnapshotAccessor snapshotAccessor,
        CancellationToken cancellationToken)
    {
        var snapshot = snapshotAccessor.Get();
        var uploadId = snapshot.UploadId;
        if (uploadId is not null)
            return uploadId;

        uploadId = await RegisterUploadId(snapshot.FileProvider.Metadata, cancellationToken).ConfigureAwait(false);
        await snapshotAccessor.Update(s => s with { UploadId = uploadId }, cancellationToken).ConfigureAwait(false);
        return uploadId;
    }

    private async Task<UploadId> RegisterUploadId(FileMetadata fileMetadata, CancellationToken cancellationToken)
    {
        var length = fileMetadata.Length;
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), fileMetadata.FileName)
            .Set(nameof(Media.Media.ContentType), fileMetadata.FileType);
        return await Commander.Call(new Uploads_Create(Session, length, "", metadata), cancellationToken).ConfigureAwait(false);
    }
}
