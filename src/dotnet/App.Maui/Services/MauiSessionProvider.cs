using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSessionProvider : ISessionResolver
{
    private const string SessionIdStorageKey = "Fusion.SessionId";
    private const string SessionIdCreatedAtStorageKey = "Fusion.SessionId.CreatedAt";
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSessionProvider)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSessionProvider>();

    private static readonly object _lock = new ();
    private static TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();

    private static TaskCompletionSource<Session> SessionSource {
        get {
            lock (_lock)
                return _sessionSource;
        }
    }

    public IServiceProvider Services { get; }

    // Explicit interface implementations
    Task<Session> ISessionResolver.SessionTask => SessionSource.Task;
    bool ISessionResolver.HasSession => SessionSource.Task.IsCompleted;

    public Session Session {
        get {
            var sessionSourceTask = SessionSource.Task;
            if (!sessionSourceTask.IsCompleted)
                throw StandardError.Internal("Session isn't initialized yet.");

 #pragma warning disable VSTHRD002
            return sessionSourceTask.Result;
 #pragma warning restore VSTHRD002
        }
        set => throw StandardError.NotSupported<MauiSessionProvider>("Session can't be set explicitly with this provider.");
    }

    public MauiSessionProvider(IServiceProvider services)
        => Services = services;

    public Task<Session> GetSession(CancellationToken cancellationToken)
        => SessionSource.Task.WaitAsync(cancellationToken);

    public static Task TryRestoreSession()
        => Task.Run(async () => {
            using var _ = Tracer.Region();
            var storedSid = await Read().ConfigureAwait(false);
            if (storedSid == null)
                return;

            var session = new Session(storedSid);
            SessionSource.TrySetResult(session);
        });

    public static void Reset(IServiceProvider services)
    {
        lock (_lock)
            _sessionSource = TaskCompletionSourceExt.New<Session>();
        var history = services.GetRequiredService<History>();
        history.ForceReload(Links.Home);
    }

    public Task CreateOrValidateSession()
        => Task.Run(async () => {
            using var _ = Tracer.Region();

            var mobileSessions = Services.GetRequiredService<IMobileSessions>();
            if (!SessionSource.Task.IsCompleted) {
                var sessionId = await mobileSessions.Create(CancellationToken.None);
                await Store(sessionId).ConfigureAwait(false);
                var session = new Session(sessionId);
                SessionSource.TrySetResult(session);
            }
            else {
                var storedSessionId = Session.Id.Value;
                var sessionId = await mobileSessions.Validate(storedSessionId, CancellationToken.None);
                if (!OrdinalEquals(sessionId, storedSessionId)) {
                    // Update sessionId and reload MAUI App container
                    lock (_lock) {
                        var session = new Session(sessionId);
                        _sessionSource = TaskCompletionSourceExt.New<Session>();
                        _sessionSource.SetResult(session);
                    }
                    await Store(sessionId).ConfigureAwait(false);
                    var application = Application.Current;
                    application!.Dispatcher.Dispatch(()
                        => (application.MainPage as MainPage)?.Reset());
                }
            }
        });

    private static async Task<string?> Read()
    {
        var storage = SecureStorage.Default;
        try {
            var storedSessionId = await storage.GetAsync(SessionIdStorageKey).ConfigureAwait(false);
            if (storedSessionId.IsNullOrEmpty())
                Log.LogInformation("No stored Session ID");
            else {
                Log.LogInformation("Successfully read stored Session ID");
                return storedSessionId;
            }
        }
        catch (Exception e)
        {
            Log.LogWarning(e, "Failed to read stored Session ID");
            // ignored
            // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            // TODO: configure selective backup, to prevent app crashes after re-installing
            // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
        }

        return null;
    }

    private static async Task Store(string sid)
    {
        var storage = SecureStorage.Default;
        bool isSaved;
        try
        {
            if (storage.Remove(SessionIdStorageKey))
                Log.LogInformation("Removed stored Session ID");
            await storage.SetAsync(SessionIdStorageKey, sid).ConfigureAwait(false);
            isSaved = true;
        }
        catch (Exception e)
        {
            isSaved = false;
            Log.LogWarning(e, "Failed to store Session ID");
            // Ignored, see:
            // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
        }

        if (!isSaved)
        {
            Log.LogInformation("Second attempt to store Session ID");
            try
            {
                storage.RemoveAll();
                await storage.SetAsync(SessionIdStorageKey, sid).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.LogWarning(e, "Failed to store Session ID (second attempt)");
                // Ignored, see:
                // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            }
        }
    }
}
