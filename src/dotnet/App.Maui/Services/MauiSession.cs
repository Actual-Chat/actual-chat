using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSession
{
    private const string SessionStorageKey = "Fusion.SessionId";
    private const string SessionCreatedAtStorageKey = "Fusion.SessionId.CreatedAt";
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSession)];
    private static readonly ILogger Log = MauiDiagnostics.LoggerFactory.CreateLogger<MauiSession>();

    private static readonly object _lock = new();
    private static volatile Task<Session?> _readSessionTask = null!;

    private IServiceProvider Services { get; }
    private TrueSessionResolver TrueSessionResolver { get; }

    public static Task Start()
        => _readSessionTask = Task.Run(Read);

    public MauiSession(IServiceProvider services)
    {
        Services = services;
        TrueSessionResolver = services.GetRequiredService<TrueSessionResolver>();
    }

    public Task<Session> Acquire()
    {
        if (TrueSessionResolver.HasSession)
            return Task.FromResult(TrueSessionResolver.Session);

        return Task.Run(async () => {
            using var _1 = Tracer.Region();

            var mobileSessions = Services.GetRequiredService<IMobileSessions>();
            var session = await _readSessionTask.ConfigureAwait(false);
            if (session == null) {
                // No session -> create one
                session = await mobileSessions.CreateSession(CancellationToken.None).ConfigureAwait(false);
                TrueSessionResolver.Session = session;
                _ = Task.Run(() => Store(session));
                return session;
            }

            // Session is there -> validate it
            TrueSessionResolver.Session = session;
            var validSession =
                await mobileSessions.ValidateSession(session, CancellationToken.None).ConfigureAwait(false);
            if (session == validSession)
                return session;

            // Session is invalid -> update it to valid + reload MAUI App
            _ = Task.Run(() => Store(validSession));
            lock (_lock) {
                _readSessionTask = Task.FromResult(validSession)!; // Just in case - we don't want to re-read it
                TrueSessionResolver.Replace(validSession);
            }
            _ = Task.Run(async () => {
                var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
                scopedServices.GetRequiredService<ReloadUI>().Reload(true);
            });
            return validSession;
        });
    }

    private static async Task<Session?> Read()
    {
        using var _ = Tracer.Region();
        var storage = SecureStorage.Default;
        try {
            var sessionId = await storage.GetAsync(SessionStorageKey).ConfigureAwait(false);
            if (!sessionId.IsNullOrEmpty()) {
                Log.LogInformation("Successfully read stored Session ID");
                return new Session(sessionId).RequireValid();
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

    private static async Task Store(Session session)
    {
        using var _ = Tracer.Region();
        var storage = SecureStorage.Default;
        bool isSaved;
        try {
            if (storage.Remove(SessionStorageKey))
                Log.LogInformation("Removed stored Session ID");
            await storage.SetAsync(SessionStorageKey, session.Id.Value).ConfigureAwait(false);
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
                await storage.SetAsync(SessionStorageKey, session.Id.Value).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to store Session ID (second attempt)");
                // Ignored, see:
                // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            }
        }
    }
}
