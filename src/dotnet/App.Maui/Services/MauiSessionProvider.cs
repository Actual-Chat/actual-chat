using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSessionProvider : ISessionResolver
{
    private const string SessionIdStorageKey = "Fusion.SessionId";
    private const string SessionIdCreatedAtStorageKey = "Fusion.SessionId.CreatedAt";
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSessionProvider)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSessionProvider>();

    private static readonly TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();
    private static readonly Task<Session> _sessionTask = _sessionSource.Task;

    public static Session Session {
        get {
            if (!_sessionTask.IsCompleted)
                throw StandardError.Internal("Session isn't initialized yet.");

 #pragma warning disable VSTHRD002
            return _sessionTask.Result;
 #pragma warning restore VSTHRD002
        }
    }

    public IServiceProvider Services { get; }

    // Explicit interface implementations
    Task<Session> ISessionResolver.SessionTask => _sessionTask;
    bool ISessionResolver.HasSession => _sessionTask.IsCompleted;
    Session ISessionResolver.Session {
        get => Session;
        set => throw StandardError.NotSupported<MauiSessionProvider>("Session can't be set explicitly with this provider.");
    }

    public MauiSessionProvider(IServiceProvider services)
    {
        Services = services;
        _ = RestoreOrCreateSession();
    }

    public Task<Session> GetSession(CancellationToken cancellationToken)
        => _sessionTask.WaitAsync(cancellationToken);

    private Task RestoreOrCreateSession()
        => Task.Run(async () => {
            using var _ = Tracer.Region();

            var storedSid = await Read().ConfigureAwait(false);
            if (storedSid == null) {
                var createSessionCommand = new MobileSessions_Create();
                var sessionId = await Services.Commander().Call(createSessionCommand);
                await Store(sessionId).ConfigureAwait(false);
                storedSid = sessionId;
            }

            var session = new Session(storedSid);
            _sessionSource.TrySetResult(session);
            return session;
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
