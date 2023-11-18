using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.App.Maui.Services;

#pragma warning disable CA1823 // Unused members - 'SessionCreatedAtStorageKey', etc.

public sealed class MauiSession(IServiceProvider services)
{
    private const string SessionStorageKey = "Fusion.SessionId";
    private const string SessionCreatedAtStorageKey = "Fusion.SessionId.CreatedAt";
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiSession)];
    private static ILogger? _log;
    private static ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger<MauiSession>();

    private static volatile Task<Session?> _readSessionTask = null!;
    private IMobileSessions? _mobileSessions;

    private IServiceProvider Services { get; } = services;
    private TrueSessionResolver TrueSessionResolver { get; } = services.GetRequiredService<TrueSessionResolver>();
    private IMobileSessions MobileSessions => _mobileSessions ??= Services.GetRequiredService<IMobileSessions>();

    public static Task Start()
        => _readSessionTask = Task.Run(Read);

    public Task Acquire()
    {
        if (TrueSessionResolver.HasSession)
            return Task.CompletedTask;

        return Task.Run(async () => {
            using var _1 = Tracer.Region();

            var session = await _readSessionTask.ConfigureAwait(false);
            if (session == null) {
                // No session -> create one
                session = await MobileSessions.CreateSession(CancellationToken.None).ConfigureAwait(false);
                TrueSessionResolver.Session = session;
                _ = Task.Run(() => Store(session));
                return;
            }

            // Session is there -> validate it
            TrueSessionResolver.Session = session;
            var validSession =
                await MobileSessions.ValidateSession(session, CancellationToken.None).ConfigureAwait(false);
            if (session == validSession)
                return;

            // Session is invalid -> update it to valid + reload MAUI App
            Log.LogWarning("Stored session is invalid - will reload");
            await Store(validSession).ConfigureAwait(false);
            TrueSessionResolver.Replace(validSession);
            Services.GetRequiredService<ReloadUI>().Reload(true, true);
        });
    }

    private static async Task<Session?> Read()
    {
        using var _ = Tracer.Region();
        var storage = SecureStorage.Default;
        try {
            var sessionId = await storage.GetAsync(SessionStorageKey).ConfigureAwait(false);
            if (!sessionId.IsNullOrEmpty()) {
                Log.LogInformation("Successfully read stored Session");
                return new Session(sessionId).RequireValid();
            }
            Log.LogInformation("No stored Session");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to read stored Session");
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
                Log.LogInformation("Removed stored Session");
            await storage.SetAsync(SessionStorageKey, session.Id.Value).ConfigureAwait(false);
            isSaved = true;
        }
        catch (Exception e) {
            isSaved = false;
            Log.LogWarning(e, "Failed to store Session");
            // Ignored, see:
            // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
        }

        if (!isSaved) {
            Log.LogInformation("Second attempt to store Session");
            try {
                storage.RemoveAll();
                await storage.SetAsync(SessionStorageKey, session.Id.Value).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to store Session (second attempt)");
                // Ignored, see:
                // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            }
        }
    }
}
