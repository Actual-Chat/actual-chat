using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSessionResolver : ISessionResolver
{
    private const string SessionIdStorageKey = "Fusion.SessionId";
    private const string SessionIdCreatedAtStorageKey = "Fusion.SessionId.CreatedAt";
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSessionResolver)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSessionResolver>();

    private static readonly object _lock = new();
    private static volatile Task<Session?> _readSessionTask = Task.Run(ReadSession);
    private static volatile TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();

    public IServiceProvider Services { get; }

    public Task<Session> SessionTask => _sessionSource.Task;
    public bool HasSession => SessionTask.IsCompleted;
    public Session Session {
        get {
            try {
                var sessionSourceTask = _sessionSource.Task;
                if (!sessionSourceTask.IsCompleted)
                    throw StandardError.Internal("Session isn't initialized yet.");

 #pragma warning disable VSTHRD002
                return sessionSourceTask.Result;
 #pragma warning restore VSTHRD002
            }
            catch (Exception e) {
                Log.LogError(e, "Session isn't initialized yet.");
                throw;
            }
        }
        set {
            try {
                throw StandardError.NotSupported<MauiSessionResolver>(
                    "Session can't be set explicitly with this provider.");
            }
            catch (Exception e) {
                Log.LogError(e, "Session can't be set explicitly with this provider.");
                throw;
            }
        }
    }

    public static Task Start()
        => _readSessionTask;

    public MauiSessionResolver(IServiceProvider services)
        => Services = services;

    public Task<Session> GetSession(CancellationToken cancellationToken = default)
        => _sessionSource.Task.WaitAsync(cancellationToken);

    public Task AcquireSession()
        => Task.Run(async () => {
            using var _1 = Tracer.Region();

            if (HasSession)
                return; // Nothing else to do

            var mobileSessions = Services.GetRequiredService<IMobileSessions>();
            var session = await _readSessionTask.ConfigureAwait(false);
            if (session == null) {
                // No session -> create one
                var sessionId = await mobileSessions.Create(CancellationToken.None).ConfigureAwait(false);
                session = new Session(sessionId);
                _sessionSource.TrySetResult(session);
                _ = Task.Run(() => StoreSession(session));
                return;
            }

            // Session is there -> validate it
            _sessionSource.TrySetResult(session); // Let's be optimists & restart if validation fails later
            var validSessionId = await mobileSessions.Validate(session.Id, CancellationToken.None).ConfigureAwait(false);
            var validSession = new Session(validSessionId);
            if (session == validSession)
                return;

            // Session is invalid -> update it to valid + reload MAUI App container.
            _ = Task.Run(() => StoreSession(validSession));
            lock (_lock) {
                _readSessionTask = Task.FromResult(validSession)!; // Just in case - we don't want to re-read it
                _sessionSource = TaskCompletionSourceExt.New<Session>().WithResult(validSession);
            }
            _ = Task.Run(async () => {
                var scopedServices = await ScopedServicesTask.ConfigureAwait(true);
                scopedServices.GetRequiredService<ReloadUI>().Reload();
            });
        });

    private static async Task<Session?> ReadSession()
    {
        using var _ = Tracer.Region();
        var storage = SecureStorage.Default;
        try {
            var sessionId = await storage.GetAsync(SessionIdStorageKey).ConfigureAwait(false);
            if (!sessionId.IsNullOrEmpty()) {
                Log.LogInformation("Successfully read stored Session ID");
                return new Session(sessionId);
            }
            Log.LogInformation("No stored Session ID");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to read stored Session ID");
            // ignored
            // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            // TODO: configure selective backup, to prevent app crashes after re-installing
            // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
        }
        return null;
    }

    private static async Task StoreSession(Session session)
    {
        using var _ = Tracer.Region();
        var storage = SecureStorage.Default;
        bool isSaved;
        try {
            if (storage.Remove(SessionIdStorageKey))
                Log.LogInformation("Removed stored Session ID");
            await storage.SetAsync(SessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
            isSaved = true;
        }
        catch (Exception e) {
            isSaved = false;
            Log.LogWarning(e, "Failed to store Session ID");
            // Ignored, see:
            // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
        }

        if (!isSaved) {
            Log.LogInformation("Second attempt to store Session ID");
            try {
                storage.RemoveAll();
                await storage.SetAsync(SessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to store Session ID (second attempt)");
                // Ignored, see:
                // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            }
        }
    }
}
