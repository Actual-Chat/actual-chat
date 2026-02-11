using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

public partial class UploadSessions : UIServiceBase<AppUIHub>
{
    private readonly UploadSessionRepo _repo;
    private readonly FileUploaderService _fileUploader;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _cleanupTask;
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new (StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new (StringComparer.Ordinal);
    private Moment Now => Clocks.SystemClock.Now;

    private SemaphoreSlim GetLock(string sessionId)
        => _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    public UploadSessions(AppUIHub hub) :base(hub)
    {
        _repo = new UploadSessionRepo(hub.Services);
        _fileUploader = new FileUploaderService(
            hub.LogFor<FileUploaderService>(),
            GetOrRegisterUpload,
            GetOrReserveMedia,
            StartProcessing,
            ClearUploadId);
        _fileUploader.ProgressChanged += OnProgressChanged;
        _fileUploader.Uploaded += OnUploaded;
        _fileUploader.Failed += OnUploadFailed;
        _cleanupTask = BackgroundTask.Run(Cleanup);
    }

    public async Task<UploadSession> CreateSession(
        IFileProvider fileProvider)
    {
        if (fileProvider == null)
            throw new ArgumentNullException(nameof(fileProvider));

        fileProvider.Initialize(Hub.Services);
        await fileProvider.PrepareForSaving().ConfigureAwait(false);

        var session = new UploadSession {
            SessionId = Guid.NewGuid().ToString(),
            FileProvider = fileProvider,
            Status = UploadStatus.Pending,
            CreatedAt = Now,
            LastUpdatedAt = Now,
        };

        await _repo.Save(session).ConfigureAwait(false);
        _sessions[session.SessionId] = session;
        LogStatusChange(session, session.Status);

        return session;
    }

    public async Task<UploadSession> ResumeSession(string sessionId)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);

            if (session.Status != UploadStatus.Completed)
                return await ResumeInternal(session).ConfigureAwait(false);

            if (session.ProgressTracker.Task.IsCompletedSuccessfully)
                return session;

            // We have a completed session, but the progress tracker has no upload result.
            // So we need to reset the session and start over.
            await ResetSessionInternal(session).ConfigureAwait(false);
            return await ResumeInternal(session).ConfigureAwait(false);
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task<UploadSession> ResumeInternal(UploadSession session)
    {
        if (session.Status is UploadStatus.Canceled)
            throw new InvalidOperationException("Cannot resume a canceled session");

        session.Status = UploadStatus.Uploading;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
        LogStatusChange(session, session.Status);

        await _fileUploader.StartOrResumeUpload(session).ConfigureAwait(false);
        return session;
    }

    public async Task<UploadSession> ResetSession(string sessionId)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            if (session.Status == UploadStatus.Canceled)
                throw new InvalidOperationException("Cannot restart a cancelled session");

            if (session.Status is not (UploadStatus.Completed or UploadStatus.Failed))
                throw new InvalidOperationException("We can only restart a completed or failed session");

            return await ResetSessionInternal(session).ConfigureAwait(false);
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task CancelSessionIfNotCompletedInternal(string sessionId, bool force)
    {
        var session = await TryGetSession(sessionId).ConfigureAwait(false);
        await _fileUploader.CancelUpload(sessionId).ConfigureAwait(false);
        if (session is null)
            return;

        session.MonitoringCts?.Cancel();

        if (session.Status is not UploadStatus.Canceled) {
            if (force || session.Status is not (UploadStatus.Completed or UploadStatus.Failed)) {
                session.LastUpdatedAt = Now;
                session.Status = UploadStatus.Canceled;
                LogStatusChange(session, session.Status);
            }
        }
        await _repo.Save(session).ConfigureAwait(false);
    }

    private async Task DeleteSessionInternal(string sessionId)
    {
        var session = await TryGetSession(sessionId).ConfigureAwait(false);
        if (session is null) {
            Log.LogWarning("Got delete session '{SessionId}' request, but did not find the session", sessionId);
            return;
        }
        if (session.Status is not UploadStatus.Canceled and not UploadStatus.Completed and not UploadStatus.Failed)
            throw new InvalidOperationException("Cannot delete a not canceled/completed/failed session");

        session.MonitoringCts?.Cancel();

        if (session.UploadId is not null)
            await Commander.Call(new Uploads_Remove(Hub.Session, session.UploadId), CancellationToken.None).ConfigureAwait(false);
        await session.FileProvider.ClearForRemoving().ConfigureAwait(false);
        await _repo.Delete(sessionId).ConfigureAwait(false);
        Hub.UploadSessionsState.Remove(sessionId);
        _sessions.TryRemove(sessionId, out _);
        _locks.TryRemove(sessionId, out _);
        Log.LogInformation("Deleted session '{SessionId}'", sessionId);
    }

    public async Task AddReference(string sessionId)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await TryGetSession(sessionId).ConfigureAwait(false);
            if (session is null)
                return;

            Interlocked.Increment(ref session.ReferenceCount);
        }
        finally {
            sessionLock.Release();
        }
    }

    public async Task ReleaseReference(string sessionId)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await TryGetSession(sessionId).ConfigureAwait(false);
            if (session is null)
                return;

            var newCount = Interlocked.Decrement(ref session.ReferenceCount);
            if (newCount > 0)
                return;

            await CancelSessionIfNotCompletedInternal(sessionId, false).ConfigureAwait(false);
            await DeleteSessionInternal(sessionId).ConfigureAwait(false);
        }
        finally {
            sessionLock.Release();
        }
    }

    public async Task<UploadSession> GetSession(string sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            throw new ArgumentException(nameof(sessionId));

        var uploadSession = await TryGetSession(sessionId).ConfigureAwait(false);
        if (uploadSession is null)
            throw new InvalidOperationException($"Upload session {sessionId} not found");

        return uploadSession;
    }

    public async Task<UploadSession?> TryGetSession(string sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            throw new ArgumentException(nameof(sessionId));

        if (_sessions.TryGetValue(sessionId, out var session))
            return session;

        session = await _repo.Get(sessionId).ConfigureAwait(false);
        if (session is null)
            return null;

        if (session.ReservedMediaId is not null)
            Hub.UploadSessionsState.SetReservedMediaId(sessionId, session.ReservedMediaId);
        session.FileProvider.Initialize(Hub.Services);
        switch (session.Status) {
            case UploadStatus.Uploaded when session.ReservedMediaId is not null:
                // Resume monitoring if session was restored in Uploaded state
                session.ProgressTracker.ReportProgress(100);
                SetProgress(session.SessionId, new UploadSessionProgress(UploadStage.Uploaded));
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                session.MonitoringCts = cts;
                _ = BackgroundTask.Run(() => MonitorProcessing(sessionId, session.ReservedMediaId, cts.Token));
                break;
            case UploadStatus.Completed when session.MediaContent is not null:
                session.ProgressTracker.ReportProgress(100);
                session.ProgressTracker.SetResult(session.MediaContent);
                SetProgress(session.SessionId, new UploadSessionProgress(UploadStage.Completed));
                break;
            case UploadStatus.Canceled:
                session.ProgressTracker.SetCanceled();
                break;
            case UploadStatus.Failed:
                session.ProgressTracker.SetException(new Exception("Upload failed"));
                SetProgress(session.SessionId, new UploadSessionProgress(UploadStage.New) { IsFailed = true, ErrorMessage ="Upload failed" });
                break;
        }
        _sessions[sessionId] = session;
        return session;
    }

    public async Task<MediaId> GetOrReserveMedia(string sessionId, CancellationToken cancellationToken)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            if (session.ReservedMediaId is not null)
                return session.ReservedMediaId;

            var metadata = await Hub.AttachmentRegistry.GetUploadSessionMetadata(sessionId, cancellationToken).ConfigureAwait(false);
            // TODO(DF): review how we choose media scope and whether we need a new media id here or not.
            var mediaScope = MediaId.GenerateScope();     //session.ChatId.Value;
            var command = new Medias_ReserveMedia(Session, mediaScope) { Metadata = metadata };
            var mediaId = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            session.ReservedMediaId = mediaId;
            session.LastUpdatedAt = Now;
            await _repo.Save(session).ConfigureAwait(false);
            Hub.UploadSessionsState.SetReservedMediaId(sessionId, mediaId);
            return session.ReservedMediaId;
        }
        finally {
            sessionLock.Release();
        }
    }

    private bool CheckIfTouched(string sessionId)
        => _sessions.ContainsKey(sessionId);

    private void LogStatusChange(UploadSession session, UploadStatus newStatus)
        => Log.LogInformation(
            "Session '{SessionId}' status changed to {Status}, file: '{FileName}'",
            session.SessionId,
            newStatus,
            session.FileName);

    private async Task<UploadSession> ResetSessionInternal(UploadSession session)
    {
        session.Status = UploadStatus.Pending;
        session.ProgressTracker = new UploadProgressTracker();
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
        LogStatusChange(session, session.Status);
        return session;
    }

    private async Task OnProgressChanged(string sessionId, double progress)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.ReportProgress(progress);
        SetProgress(sessionId, new UploadSessionProgress(UploadStage.Uploading, progress));
    }

    private async Task OnUploaded(string sessionId, UploadId uploadId, MediaId mediaId)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            session.ProgressTracker.ReportProgress(100);
            session.Status = UploadStatus.Uploaded;
            session.LastUpdatedAt = Now;
            await _repo.Save(session).ConfigureAwait(false);
            LogStatusChange(session, session.Status);
            SetProgress(sessionId, new UploadSessionProgress(UploadStage.Uploaded));

            // Start monitoring processing status in background
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            session.MonitoringCts = cts;
            _ = BackgroundTask.Run(() => MonitorProcessing(sessionId, mediaId, cts.Token));
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task OnProcessingCompleted(string sessionId, MediaContent mediaContent)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            session.ProgressTracker.SetResult(mediaContent);
            session.Status = UploadStatus.Completed;
            session.LastUpdatedAt = Now;
            session.MediaContent = mediaContent;
            await _repo.Save(session).ConfigureAwait(false);
            LogStatusChange(session, session.Status);
            SetProgress(sessionId, new UploadSessionProgress(UploadStage.Completed));
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task OnFailed(string sessionId, Exception error)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync().ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            session.ProgressTracker.SetException(error);
            session.Status = UploadStatus.Failed;
            session.LastUpdatedAt = Now;
            await _repo.Save(session).ConfigureAwait(false);
            LogStatusChange(session, session.Status);
            var errorMessage = error.Message.IsNullOrEmpty() ? "Upload failed" : error.Message;
            var uploadProgress = await Hub.UploadSessionsState.GetProgress(sessionId, default);
            uploadProgress = uploadProgress with {
                IsFailed = true,
                ErrorMessage = "Failed: " + errorMessage,
            };
            SetProgress(sessionId, uploadProgress);
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task<UploadId> GetOrRegisterUpload(string sessionId, CancellationToken cancellationToken)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            if (session.UploadId is not null)
                return session.UploadId;

            if (session.Status is not UploadStatus.Uploading)
                throw new InvalidOperationException("Cannot register an upload for a session that is not uploading");

            var fileMetadata = session.FileProvider.Metadata;
            var length = fileMetadata.Length;
            var metadata = new PropertyBag()
                .Set(nameof(Media.Media.FileName), fileMetadata.FileName)
                .Set(nameof(Media.Media.ContentType), fileMetadata.FileType);
            var uploadId = await Commander.Call(new Uploads_Create(Hub.Session, length, "", metadata), cancellationToken).ConfigureAwait(false);
            session.UploadId = uploadId;
            session.LastUpdatedAt = Now;
            await _repo.Save(session).ConfigureAwait(false);
            return session.UploadId;
        }
        finally {
            sessionLock.Release();
        }
    }

    private Task StartProcessing(UploadId uploadId, MediaId mediaId, CancellationToken ct)
        => Commander.Call(new Uploads_StartProcessUpload(Session, uploadId, mediaId), ct);

    private async Task ClearUploadId(string sessionId, CancellationToken cancellationToken)
    {
        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var session = await GetSession(sessionId).ConfigureAwait(false);
            if (session.UploadId is null)
                return;
            session.UploadId = null;
            session.LastUpdatedAt = Now;
            await _repo.Save(session).ConfigureAwait(false);
        }
        finally {
            sessionLock.Release();
        }
    }

    private async Task OnUploadFailed(string sessionId, Exception error)
    {
        Log.LogError(error, "Upload failed for session {SessionId}", sessionId);
        await OnFailed(sessionId, error).ConfigureAwait(false);
    }

    private async Task MonitorProcessing(string sessionId, MediaId mediaId, CancellationToken cancellationToken)
    {
        try {
            var cStatus = await Computed
                .Capture(() => Hub.Medias.GetStatus(Session, mediaId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            var changes = cStatus.Changes(FixedDelayer.NoneUnsafe, cancellationToken);
            await foreach (var c in changes.ConfigureAwait(false)) {
                var (status, error) = c;
                if (error != null) {
                    Log.LogWarning(error, "Error getting status for media {MediaId}", mediaId);
                    continue;
                }
                if (status == null) {
                    await OnProcessingFailed(new InvalidOperationException("Media not found")).ConfigureAwait(false);
                    return;
                }

                if (status.HasFailed) {
                    await OnProcessingFailed(new InvalidOperationException(status.ErrorMessage)).ConfigureAwait(false);
                    return;
                }

                if (status.Stage is MediaStage.Ready) {
                    var content = await Hub.Medias.GetContent(Session, mediaId, CancellationToken.None).ConfigureAwait(false);
                    if (content == null) {
                        await OnProcessingFailed(new InvalidOperationException("Media content not found after Ready status")).ConfigureAwait(false);
                        return;
                    }
                    await OnProcessingCompleted(sessionId, content).ConfigureAwait(false);
                    return;
                }
            }

            await OnProcessingFailed(new TimeoutException("Media processing timed out")).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // Session was cancelled - exit without marking as failed
            Log.LogInformation("Monitoring cancelled for session {SessionId}", sessionId);
        }
        catch (Exception e) {
            Log.LogError(e, "Error monitoring processing for session {SessionId}", sessionId);
            await OnProcessingFailed(e).ConfigureAwait(false);
        }

        async Task OnProcessingFailed(Exception error)
        {
            Log.LogError(error, "Processing failed for session {SessionId}", sessionId);
            await OnFailed(sessionId, error).ConfigureAwait(false);
        }
    }

    private void SetProgress(string sessionId, UploadSessionProgress progress)
        => Hub.UploadSessionsState.SetProgress(sessionId, progress);
}
