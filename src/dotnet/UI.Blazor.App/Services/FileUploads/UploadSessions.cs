namespace ActualChat.UI.Blazor.App.Services;

using System.Collections.Concurrent;

public class UploadSessions : UIServiceBase<AppUIHub>
{
    private readonly IUploadSessionRepository _repository;
    private readonly FileUploaderService _fileUploader;
    private readonly ILogger _log;

    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new (StringComparer.Ordinal);
    private Moment Now => Clocks.SystemClock.Now;

    public UploadSessions(AppUIHub hub) :base(hub)
    {
        _repository = hub.Services.GetRequiredService<IUploadSessionRepository>();
        _fileUploader = hub.Services.GetRequiredService<FileUploaderService>();
        _log = hub.LogFor<UploadSessions>();

        _fileUploader.ProgressChanged += OnProgressChanged;
        _fileUploader.Completed += OnCompleted;
        _fileUploader.Failed += OnFailed;
        _fileUploader.Canceled += OnCanceled;
    }

    #region Public API

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
            FileId = Guid.NewGuid().ToString(),
            FileProvider = fileProvider,
            Status = UploadStatus.Pending,
            CreatedAt = Now,
            LastUpdatedAt = Now,
            ChatId = chatId,
        };

        _sessions[session.SessionId] = session;
        await _repository.Save(session).ConfigureAwait(false);
        // var copy = await _repository.Get(session.SessionId).ConfigureAwait(false);

        _log.LogInformation("Created session {SessionId}", session.SessionId);

        return session;
    }

    public async Task ResumeSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);

        if (session.Status == UploadStatus.Completed)
            return;

        if (session.Status == UploadStatus.Canceled)
            throw new InvalidOperationException("Cannot resume a cancelled session");

        session.Status = UploadStatus.Uploading;
        session.LastUpdatedAt = Now;
        await _repository.Save(session).ConfigureAwait(false);

        await _fileUploader.StartOrResumeUpload(session).ConfigureAwait(false);
    }

    public async Task CancelSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        await _fileUploader.CancelUpload(sessionId).ConfigureAwait(false);

        session.Status = UploadStatus.Canceled;
        session.LastUpdatedAt = Now;
        await _repository.Save(session).ConfigureAwait(false);
    }

    public async Task DeleteSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        if (session.Status is not UploadStatus.Canceled and not UploadStatus.Completed and not UploadStatus.Failed)
            throw new InvalidOperationException("Cannot delete a not canceled/completed/failed session");

        await session.FileProvider.ClearBeforeRemoving().ConfigureAwait(false);
        await _repository.Delete(sessionId).ConfigureAwait(false);
        _log.LogInformation("Deleted session {SessionId}", sessionId);
    }

    public async Task<IEnumerable<UploadSession>> GetActiveSessionsAsync()
        => await _repository.GetAll().ConfigureAwait(false);
    #endregion

    #region Event Handlers

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
        await _repository.Save(session).ConfigureAwait(false);
    }

    private async Task OnFailed(string sessionId, Exception error)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.SetException(error);
        session.Status = UploadStatus.Failed;
        session.LastUpdatedAt = Now;
        await _repository.Save(session).ConfigureAwait(false);
    }

    private async Task OnCanceled(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.ProgressTracker.SetCanceled();
        session.Status = UploadStatus.Canceled;
        session.LastUpdatedAt = Now;
        await _repository.Save(session).ConfigureAwait(false);
    }

    #endregion

    #region Helpers

    public async Task<UploadSession> GetSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            return session;

        session = await _repository.Get(sessionId).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"Upload session {sessionId} not found");

        session.FileProvider.Initialize(Hub.Services);
        _sessions[sessionId] = session;
        return session;
    }

    #endregion
}
