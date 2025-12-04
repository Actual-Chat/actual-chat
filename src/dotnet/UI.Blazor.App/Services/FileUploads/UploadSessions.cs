using ActualChat.Media;

namespace ActualChat.UI.Blazor.App.Services;

using System.Collections.Concurrent;

public partial class UploadSessions : UIServiceBase<AppUIHub>
{
    private readonly UploadSessionRepo _repo;
    private readonly FileUploaderService _fileUploader;
    // ReSharper disable once NotAccessedField.Local
    private readonly Task _cleanupTask;

    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new (StringComparer.Ordinal);
    private Moment Now => Clocks.SystemClock.Now;

    public UploadSessions(AppUIHub hub) :base(hub)
    {
        _repo = new UploadSessionRepo(hub.Services);
        _fileUploader = new FileUploaderService(hub.Services, GetOrRegisterUpload, ConvertUpload);

        _fileUploader.ProgressChanged += OnProgressChanged;
        _fileUploader.Completed += OnCompleted;
        _fileUploader.Failed += OnFailed;
        _fileUploader.Canceled += OnCanceled;

        _cleanupTask = BackgroundTask.Run(Cleanup);
    }

    public async Task<UploadSession> CreateSession(
        ChatId chatId,
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
            ChatId = chatId,
        };

        _sessions[session.SessionId] = session;
        await _repo.Save(session).ConfigureAwait(false);

        Log.LogInformation("Created session {SessionId}", session.SessionId);

        return session;
    }

    public async Task<UploadSession> ResumeSession(string sessionId)
    {
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

    private async Task<UploadSession> ResumeInternal(UploadSession session)
    {
        if (session.Status is UploadStatus.Canceled)
            throw new InvalidOperationException("Cannot resume a canceled session");

        session.Status = UploadStatus.Uploading;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);

        await _fileUploader.StartOrResumeUpload(session).ConfigureAwait(false);
        return session;
    }

    public async Task<UploadSession> ResetSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        if (session.Status == UploadStatus.Canceled)
            throw new InvalidOperationException("Cannot restart a cancelled session");

        if (session.Status is not (UploadStatus.Completed or UploadStatus.Failed))
            throw new InvalidOperationException("We can only restart a completed or failed session");

        return await ResetSessionInternal(session).ConfigureAwait(false);
    }

    public Task CancelSession(string sessionId)
        => CancelSessionIfNotCompleted(sessionId, true);

    public Task CancelSessionIfNotCompleted(string sessionId)
        => CancelSessionIfNotCompleted(sessionId, false);

    private async Task CancelSessionIfNotCompleted(string sessionId, bool force)
    {
        var session = await TryGetSession(sessionId).ConfigureAwait(false);
        await _fileUploader.CancelUpload(sessionId).ConfigureAwait(false);
        if (session is null)
            return;

        if (session.Status is not UploadStatus.Canceled) {
            if (force || session.Status is not (UploadStatus.Completed or UploadStatus.Failed)) {
                session.LastUpdatedAt = Now;
                session.Status = UploadStatus.Canceled;
            }
        }
        await _repo.Save(session).ConfigureAwait(false);
    }

    public async Task DeleteSession(string sessionId)
    {
        var session = await TryGetSession(sessionId).ConfigureAwait(false);
        if (session is null) {
            Log.LogWarning("Got delete session '{SessionId}' request, but did not find the session", sessionId);
            return;
        }
        if (session.Status is not UploadStatus.Canceled and not UploadStatus.Completed and not UploadStatus.Failed)
            throw new InvalidOperationException("Cannot delete a not canceled/completed/failed session");

        if (session.UploadId is not null)
            await Hub.Commander.Call(new Uploads_Remove(Hub.Session, session.UploadId), CancellationToken.None).ConfigureAwait(false);
        await session.FileProvider.ClearForRemoving().ConfigureAwait(false);
        await _repo.Delete(sessionId).ConfigureAwait(false);
        Log.LogInformation("Deleted session '{SessionId}'", sessionId);
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

        session.FileProvider.Initialize(Hub.Services);
        _sessions[sessionId] = session;
        return session;
    }

    private bool CheckIfTouched(string sessionId)
        => _sessions.ContainsKey(sessionId);

    private async Task<UploadSession> ResetSessionInternal(UploadSession session)
    {
        session.Status = UploadStatus.Pending;
        session.ProgressTracker = new UploadProgressTracker();
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
        return session;
    }

    private async Task OnProgressChanged(string sessionId, double progress)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.ReportProgress(progress);
    }

    private async Task OnCompleted(string sessionId, MediaContent mediaContent)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.ReportProgress(100);
        session.ProgressTracker.SetResult(mediaContent);
        session.Status = UploadStatus.Completed;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
    }

    private async Task OnFailed(string sessionId, Exception error)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.SetException(error);
        session.Status = UploadStatus.Failed;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
    }

    private async Task OnCanceled(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.SetCanceled();
        session.Status = UploadStatus.Canceled;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
    }

    private async Task<UploadId> GetOrRegisterUpload(string sessionId, CancellationToken cancellationToken)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        if (session.UploadId is not null)
            return session.UploadId;

        if (session.Status is not UploadStatus.Uploading)
            throw new InvalidOperationException("Cannot register an upload for a session that is not uploading");
        var tag = nameof(TextEntryAttachment) + "/v1/" + session.ChatId.Value;
        var fileMetadata = session.FileProvider.Metadata;
        var length = fileMetadata.Length;
        var metadata = new PropertyBag()
            .Set(nameof(Media.Media.FileName), fileMetadata.FileName)
            .Set(nameof(Media.Media.ContentType), fileMetadata.FileType);
        var uploadId = await Commander.Call(new Uploads_Create(Hub.Session, length, tag, metadata), cancellationToken).ConfigureAwait(false);
        session.UploadId = uploadId;
        session.LastUpdatedAt = Now;
        await _repo.Save(session).ConfigureAwait(false);
        return session.UploadId;
    }

    private Task<MediaContent> ConvertUpload(UploadId uploadId, CancellationToken ct)
        => UICommander.Call(new Uploads_ConvertToMediaContent(Session, uploadId), ct);
}
