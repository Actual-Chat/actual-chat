namespace ActualChat.UI.Blazor.App.Services;

public partial class UploadSessions : UIServiceBase<AppUIHub>
{
    private readonly Task _cleanupTask;
    private readonly UploadSessionRepo _repo;
    private readonly UploadOperations _uploadOperations;
    private readonly UploadSessionsState _uploadSessionsState;
    private readonly ConcurrentDictionary<string, SessionHolder> _sessions = new (StringComparer.Ordinal);
    private readonly Func<UploadSessionSnapshot, CancellationToken, Task> _storage;

    public UploadSessions(AppUIHub hub) :base(hub)
    {
        _repo = new UploadSessionRepo(hub.Services);
        _uploadOperations = new UploadOperations(hub);
        _uploadSessionsState = hub.UploadSessionsState;
        _cleanupTask = BackgroundTask.Run(Cleanup);
        _storage = CreateStorage();
    }

    public async Task<string> CreateSession(IFileProvider fileProvider, PropertyBag metadata)
    {
        if (fileProvider == null)
            throw new ArgumentNullException(nameof(fileProvider));

        fileProvider.Initialize(Hub.Services);
        await fileProvider.PrepareForSaving().ConfigureAwait(false);

        var now = _uploadOperations.Now();
        var snapshot = CreateNewUploadSessionSnapshot(fileProvider, metadata, now);
        var storage = CreateStorage();
        var session = new UploadSession(snapshot, _uploadOperations, storage);
        await _repo.Save(snapshot).ConfigureAwait(false);
        _sessions[session.SessionId] = new SessionHolder(session);
        return session.SessionId;
    }

    public bool Start(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionHolder))
            return false;

        sessionHolder.Session.Start();
        return true;
    }

    public async Task<UploadSession?> TryGetSession(string sessionId)
    {
        if (sessionId.IsNullOrEmpty())
            throw new ArgumentException(nameof(sessionId));

        if (_sessions.TryGetValue(sessionId, out var sessionHolder))
            return sessionHolder.Session;

        var snapshot = await _repo.Get(sessionId).ConfigureAwait(false);
        if (snapshot is null)
            return null;

        snapshot.FileProvider.Initialize(Hub.Services);
        var session = new UploadSession(snapshot, _uploadOperations, CreateStorage());
        _sessions[sessionId] = new SessionHolder(session);
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

    private static UploadSessionSnapshot CreateNewUploadSessionSnapshot(IFileProvider fileProvider, PropertyBag metadata,
        Moment now)
    {
        var snapshot = new UploadSessionSnapshot {
            SessionId = Guid.NewGuid().ToString(),
            FileProvider = fileProvider,
            Metadata = metadata,
            CurrentState = UploadSessionState.Created,
            DataVersion = 1,
            CreatedAt = now,
            LastUpdatedAt = now,
        };
        return snapshot;
    }

    public void AddReference(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionHolder))
            throw new InvalidOperationException($"Session {sessionId} not found");

        Interlocked.Increment(ref sessionHolder.ReferenceCount);
    }

    public void ReleaseReference(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var sessionHolder))
            throw new InvalidOperationException($"Session {sessionId} not found");

        var newCount = Interlocked.Decrement(ref sessionHolder.ReferenceCount);
        if (newCount != 0)
            return;

        var session = sessionHolder.Session;
        var completed = session.Cancel();
        _ = BackgroundTask.Run( async () => {
            await completed.ConfigureAwait(false);
            await DeleteSessionInternal(session).ConfigureAwait(false);
        });
    }

    private async Task DeleteSessionInternal(UploadSession session)
    {
        var sessionId = session.SessionId;
        if (session.UploadId is {} uploadId)
            await _uploadOperations.RemoveUpload(uploadId, CancellationToken.None).ConfigureAwait(false);
        await session.FileProvider.ClearForRemoving().ConfigureAwait(false);
        await _repo.Delete(session.SessionId).ConfigureAwait(false);
        Hub.UploadSessionsState.Remove(sessionId);
        _sessions.TryRemove(sessionId, out _);
        Log.LogInformation("Deleted session '{SessionId}'", sessionId);
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
        };
    }

    private void SetProgress(string sessionId, UploadSessionProgress progress)
        => Hub.UploadSessionsState.SetProgress(sessionId, progress);

    private bool CheckIfTouched(string sessionId)
        => _sessions.ContainsKey(sessionId);

    // Nested types
    public class SessionHolder(UploadSession session)
    {
        public UploadSession Session { get; } = session;
        public long ReferenceCount;
    }
}
