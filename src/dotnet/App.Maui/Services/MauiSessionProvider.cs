using System.Net;
using System.Security.Authentication;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSessionProvider : ISessionProvider
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSessionProvider)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSessionProvider>();

    private static readonly TaskCompletionSource<Session> _sessionSource = TaskCompletionSourceExt.New<Session>();
    private static readonly Task<Session> _sessionTask = _sessionSource.Task;

    public static Task WhenSessionReady => _sessionSource.Task;

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
    Session ISessionResolver.Session => Session;
    Session ISessionProvider.Session {
        get => Session;
        set => throw StandardError.NotSupported<MauiSessionProvider>("Session can't be set explicitly with this provider.");
    }

    public MauiSessionProvider(IServiceProvider services)
        => Services = services;

    Task<Session> ISessionResolver.GetSession(CancellationToken cancellationToken)
        => GetSession(cancellationToken);
    public static Task<Session> GetSession(CancellationToken cancellationToken = default)
        => _sessionTask.WaitAsync(cancellationToken);

    public static Task RestoreOrCreate()
        => Task.Run(async () => {
            using var _ = Tracer.Region(nameof(RestoreOrCreate));

            const string sessionIdStorageKey = "Fusion.SessionId";
            var session = (Session?)null;

            var storage = SecureStorage.Default;
            try {
                var storedSessionId = await storage.GetAsync(sessionIdStorageKey).ConfigureAwait(false);
                if (!storedSessionId.IsNullOrEmpty()) {
                    session = new Session(storedSessionId);
                    Log.LogInformation("Successfully read stored Session ID");
                }
                else
                    Log.LogInformation("No stored Session ID");
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to read stored Session ID");
                // ignored
                // https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                // TODO: configure selective backup, to prevent app crashes after re-installing
                // https://learn.microsoft.com/en-us/xamarin/essentials/secure-storage?tabs=android#selective-backup
            }

            if (session == null) {
                var sessionId = await CreateSessionId().ConfigureAwait(false);
                session = new Session(sessionId);
                bool isSaved;
                try {
                    if (storage.Remove(sessionIdStorageKey))
                        Log.LogInformation("Removed stored Session ID");
                    await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
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
                        await storage.SetAsync(sessionIdStorageKey, session.Id.Value).ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Log.LogWarning(e, "Failed to store Session ID (second attempt)");
                        // Ignored, see:
                        // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
                    }
                }
            }

            _sessionSource.TrySetResult(session);
            return session;
        });

    private static async Task<string> CreateSessionId()
    {
        var _ = Tracer.Region(nameof(CreateSessionId));
        try {
            // Manually configure HTTP client as we don't have it configured globally at DI level
            using var httpClient = new HttpClient(new HttpClientHandler {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                UseCookies = false,
            }, true) {
                DefaultRequestVersion = HttpVersion.Version30,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            var gclbCookieHeader = AppLoadBalancerSettings.Default.GclbCookieHeader;
            httpClient.DefaultRequestHeaders.Add(gclbCookieHeader.Name, gclbCookieHeader.Value);

            var authClientLogger = MauiDiagnostics.LoggerFactory.CreateLogger<MobileAuthClient>();
            var authClient = new MobileAuthClient(httpClient, authClientLogger);
            var sessionId = await authClient.GetOrCreateSessionId().ConfigureAwait(false);
            return sessionId;
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(CreateSessionId)} failed");
            throw;
        }
    }
}
