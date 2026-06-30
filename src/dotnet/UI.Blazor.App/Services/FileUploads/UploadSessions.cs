using ActualLab.IO;

namespace ActualChat.UI.Blazor.App.Services;

public partial class UploadSessions : UIServiceBase<AppUIHub>
{
    private readonly Task _cleanupTask;
    private readonly IUploadSessionRepo _repo;
    private readonly UploadOperations _uploadOperations;
    private readonly ConcurrentDictionary<string, SessionRef> _sessions = new ();
    private readonly Func<UploadSessionSnapshot, CancellationToken, Task> _storage;

    private UploadSessionsState UploadSessionsState => Hub.UploadSessionsState;

    public UploadSessions(AppUIHub hub) :base(hub)
    {
        _repo = hub.Services.GetRequiredService<IUploadSessionRepo>();
        _uploadOperations = new UploadOperations(hub);
        _cleanupTask = BackgroundTask.Run(Cleanup);
        _storage = CreateStorage();
    }

    public async Task<string> CreateSession(IFileProvider fileProvider, PropertyBag metadata, string mediaScope)
    {
        if (fileProvider == null)
            throw new ArgumentNullException(nameof(fileProvider));

        fileProvider.Initialize(Hub.Services);
        await fileProvider.PrepareForSaving().ConfigureAwait(false);

        var now = _uploadOperations.Now();
        var snapshot = UploadSession.NewUploadSnapshot(fileProvider, metadata, now, mediaScope);
        var session = new UploadSession(snapshot, _uploadOperations, _storage);
        // Register in memory before persisting, so cleanup never reclaims it before the caller references it.
        _sessions[session.SessionId] = new SessionRef(session);
        await _repo.Save(snapshot).ConfigureAwait(false);
        return session.SessionId;
    }

    public bool Resume(string sessionId)
        => _sessions.TryGetValue(sessionId, out var sessionRef) && sessionRef.Session.Resume();

    public async Task<UploadSession?> TryGetSession(string sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            throw new ArgumentException(nameof(sessionId));

        if (_sessions.TryGetValue(sessionId, out var sessionRef))
            return sessionRef.Session;

        var snapshot = await _repo.Get(sessionId).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        snapshot.FileProvider.Initialize(Hub.Services);
        var session = new UploadSession(snapshot, _uploadOperations, _storage);
        _sessions[sessionId] = new SessionRef(session);
        SetProgress(sessionId, GetProgressFromSnapshot(snapshot));
        return session;
    }

    public async Task<MediaId> GetOrReserveMedia(string sessionId, CancellationToken cancellationToken)
    {
        var session = await TryGetSession(sessionId).ConfigureAwait(false);
        if (session is null)
            throw new InvalidOperationException($"Session {sessionId} not found");

        return await session.WhenMediaReserved.ConfigureAwait(false);
    }

    public void AddReference(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionRef))
            throw new InvalidOperationException($"Session {sessionId} not found");

        Interlocked.Increment(ref sessionRef.ReferenceCount);
    }

    public void ReleaseReference(string sessionId, bool cancel = true)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionRef))
            throw new InvalidOperationException($"Session {sessionId} not found");

        var newCount = Interlocked.Decrement(ref sessionRef.ReferenceCount);
        if (newCount != 0)
            return;

        if (!cancel)
            return;

        ReleaseSessionInternal(sessionRef.Session);
    }

    public async Task DeleteStaleSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var sessionRef) && sessionRef.ReferenceCount > 0)
            throw StandardError.Constraint($"Session {sessionId} is active");

        if (sessionRef is not null) {
            ReleaseSessionInternal(sessionRef.Session);
            return;
        }

        var snapshot = await _repo.Get(sessionId).ConfigureAwait(false);
        if (snapshot is null)
            return;

        var fileProvider = snapshot.FileProvider;
        fileProvider.Initialize(Hub.Services);
        await DeleteSessionResources(
            sessionId,
            fileProvider,
            snapshot.TranscodedFilePath,
            snapshot.UploadId).ConfigureAwait(false);
        Log.LogDebug("Deleted stale session '{SessionId}' ('{FileName}')", sessionId, fileProvider.Metadata.FileName);
    }

    private void ReleaseSessionInternal(UploadSession session)
    {
        Log.LogDebug("Releasing reference for session '{SessionId}' ('{FileName}')", session.SessionId, session.FileProvider.Metadata.FileName);
        var completed = session.Cancel();
        _ = BackgroundTask.Run( async () => {
            await completed.WaitAsync(TimeSpan.FromSeconds(30)).SilentAwait(false);
            await DeleteSessionInternal(session).ConfigureAwait(false);
        });
    }

    private async Task DeleteSessionInternal(UploadSession session)
    {
        var sessionId = session.SessionId;
        _sessions.TryRemove(sessionId, out _);
        var fileProvider = session.FileProvider;
        await DeleteSessionResources(
            sessionId,
            fileProvider,
            session.TranscodedFilePath,
            session.UploadId).ConfigureAwait(false);
        Log.LogDebug("Deleted session '{SessionId}' ('{FileName}')", sessionId, fileProvider.Metadata.FileName);
    }

    private Func<UploadSessionSnapshot, CancellationToken, Task> CreateStorage()
    {
        Func<UploadSessionSnapshot, CancellationToken, Task> storage = async (s, _) => {
            await _repo.Save(s).ConfigureAwait(false);
            if (s.CurrentState != UploadSessionState.Cancelled)
                SetProgress(s.SessionId, GetProgressFromSnapshot(s));
        };
        return storage;
    }

    private static UploadSessionProgress GetProgressFromSnapshot(UploadSessionSnapshot s)
    {
        var progress = 0d;
        if (s.CurrentState is
            UploadSessionState.ClientProcessing
            or UploadSessionState.Uploading
            or UploadSessionState.ServerProcessing)
            progress = s.StageProgress;

        var uploadStage = s.CurrentState switch {
            UploadSessionState.Created => UploadStage.New,
            UploadSessionState.Initializing => UploadStage.New,
            UploadSessionState.ClientProcessing => UploadStage.ClientProcessing,
            UploadSessionState.Uploading => UploadStage.Uploading,
            UploadSessionState.ServerProcessing => UploadStage.ServerProcessing,
            UploadSessionState.Completed => UploadStage.Completed,
            _ => throw new InvalidOperationException($"Unknown upload session state: {s.CurrentState}")
        };

        return new UploadSessionProgress(uploadStage, progress) {
            IsFailed = s.IsFailed,
            TotalBytes = s.FileProvider.Metadata.Length,
        };
    }

    private void SetProgress(string sessionId, UploadSessionProgress progress)
        => UploadSessionsState.SetProgress(sessionId, progress);

    private bool CheckIfActive(string sessionId)
        => _sessions.ContainsKey(sessionId);

    private async Task DeleteSessionResources(string sessionId, IFileProvider fileProvider, string? transcodedFilePath, UploadId? uploadId)
    {
        if (uploadId is not null)
            await _uploadOperations.RemoveUpload(uploadId, CancellationToken.None).ConfigureAwait(false);
        await fileProvider.ClearForRemoving().ConfigureAwait(false);
        DeleteFile(transcodedFilePath);
        await _repo.Delete(sessionId).ConfigureAwait(false);
        UploadSessionsState.Remove(sessionId);
    }

    private static void DeleteFile(FilePath? filePath)
    {
        if (filePath?.IsEmpty != false || !File.Exists(filePath))
            return;

        File.Delete(filePath);
    }

    // Nested types
    private class SessionRef(UploadSession session)
    {
        public UploadSession Session { get; } = session;
        public long ReferenceCount;
    }
}
