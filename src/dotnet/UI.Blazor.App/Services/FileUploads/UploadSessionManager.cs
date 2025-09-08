namespace ActualChat.UI.Blazor.App.Services;

using System.Collections.Concurrent;

public class UploadSessionManager
{
    private readonly IUploadSessionRepository _repository;
    private readonly IFileUploaderService _fileUploader;

    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new (StringComparer.Ordinal);
    private Moment Now => Moment.Now;

    public UploadSessionManager(IUploadSessionRepository repository, IFileUploaderService fileUploader)
    {
        _repository = repository;
        _fileUploader = fileUploader;

        _fileUploader.OnProgress += HandleProgress;
        _fileUploader.OnCompleted += HandleCompleted;
        _fileUploader.OnFailed += HandleFailed;
    }

    #region Public API

    public async Task<UploadSession> CreateSession(
        ChatId chatId,
        IFileProvider fileProvider)
    {
        if (fileProvider == null)
            throw new ArgumentNullException(nameof(fileProvider));

        await fileProvider.PrepareForSaving().ConfigureAwait(false);

        var session = new UploadSession
        {
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

        return session;
    }

    public async Task ResumeSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);

        if (session.Status == UploadStatus.Completed)
            return;

        if (session.Status == UploadStatus.Cancelled)
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

        session.Status = UploadStatus.Cancelled;
        session.LastUpdatedAt = Now;
        await _repository.Save(session).ConfigureAwait(false);
    }

    public async Task DeleteSession(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        if (session.Status is not UploadStatus.Cancelled and not UploadStatus.Completed and not UploadStatus.Failed)
            throw new InvalidOperationException("Cannot delete a not canceled/completed/failed session");

        await session.FileProvider.ClearBeforeRemoving().ConfigureAwait(false);
        await _repository.Delete(sessionId).ConfigureAwait(false);
    }

    public async Task<IEnumerable<UploadSession>> GetActiveSessionsAsync()
        => await _repository.GetAll().ConfigureAwait(false);
    #endregion

    #region Event Handlers

    private async Task HandleProgress(string sessionId, int uploadedChunks, int totalChunks)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.LastUpdatedAt = DateTime.UtcNow;

        var percent = (double)uploadedChunks / totalChunks * 100;
        Console.WriteLine($"Progress {session.FileName}: {percent:0.0}%");

        await _repository.Save(session).ConfigureAwait(false);
    }

    private async Task HandleCompleted(string sessionId)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.Status = UploadStatus.Completed;
        //session.UploadedChunks = session.TotalChunks;
        session.LastUpdatedAt = DateTime.UtcNow;
        await _repository.Save(session).ConfigureAwait(false);
    }

    private async Task HandleFailed(string sessionId, string error)
    {
        var session = await GetSession(sessionId).ConfigureAwait(false);
        session.Status = UploadStatus.Failed;
        session.LastUpdatedAt = DateTime.UtcNow;
        Console.WriteLine($"Upload failed: {error}");
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

        _sessions[sessionId] = session;
        return session;
    }

    #endregion
}
