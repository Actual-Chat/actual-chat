using ActualChat.Security;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSession(IServiceProvider services)
{
    private const string SessionStorageKey = "Fusion.SessionId";
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiSession)];
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<MauiSession>();

    private static readonly Lock Lock = new();
    private static Task<Session?>? _readSessionTask;

    private IServiceProvider Services { get; } = services;
    private TrueSessionResolver TrueSessionResolver { get; } = services.GetRequiredService<TrueSessionResolver>();
    private IMobileSessions MobileSessions => field ??= Services.GetRequiredService<IMobileSessions>();

    private static ISecureStorage Storage
#if IOS || MACCATALYST
        => field ??= AppleSharedSecureStorage.Default;
#else
        => field ??= SecureStorage.Default;
#endif

    public static Task Start()
        => ReadStored();

    public static Task<Session?> ReadStored()
    {
        lock (Lock)
            return _readSessionTask ??= Task.Run(Read);
    }

    public Task Acquire()
    {
        if (TrueSessionResolver.HasSession)
            return Task.CompletedTask;

        return Task.Run(async () => {
            using var _1 = Tracer.MethodRegion();

            var session = await ReadStored().ConfigureAwait(false);
            if (session == null) {
                // No session -> create one
                session = await MobileSessions.CreateSession(MauiSettings.AppUserAgent, CancellationToken.None).ConfigureAwait(false);
                TrueSessionResolver.Session = session;
                _ = Task.Run(() => Store(session));
                return;
            }

            // Session is there -> validate it
            TrueSessionResolver.Session = session;
            var validSession =
                await MobileSessions.ValidateSession(session, MauiSettings.AppUserAgent, CancellationToken.None).ConfigureAwait(false);
            if (session == validSession)
                return;

            // Session is invalid -> update it to valid + reload MAUI App
            Log.LogWarning("Stored session is invalid - will reload");
            await Store(validSession).ConfigureAwait(false);
            TrueSessionResolver.Replace(validSession);
            // Reloading mid-startup recreates the WebView while the first Blazor scope is
            // still initializing, leaving a half-disposed scope with a disconnected JS runtime
            // (=> endless JSDisconnectedException in the post-reload sign-in UI). Wait until the
            // initial scope is fully rendered so this becomes a clean, single reload instead.
            try {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await AppServicesAccessor.WhenBlazorAppServicesReady(true, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                Log.LogWarning("Timed out waiting for the app to render before reload - reloading anyway");
            }
            Services.GetRequiredService<ReloadUI>().Reload(true, true);
        });
    }

    public static Task RemoveStored()
    {
        using var _ = Tracer.MethodRegion();
        try {
            if (Storage.Remove(SessionStorageKey))
                Log.LogInformation("Removed stored Session");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to remove Session");
            // Ignored, see:
            // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
        }
        return Task.CompletedTask;
    }

    private static async Task<Session?> Read()
    {
        using var _ = Tracer.MethodRegion();
        var storage = Storage;
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
        using var _ = Tracer.MethodRegion();
        bool isSaved;
        try {
            if (Storage.Remove(SessionStorageKey))
                Log.LogInformation("Removed stored Session before saving");
            await Storage.SetAsync(SessionStorageKey, session.Id).ConfigureAwait(false);
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
                Storage.RemoveAll();
                await Storage.SetAsync(SessionStorageKey, session.Id).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to store Session (second attempt)");
                // Ignored, see:
                // - https://learn.microsoft.com/en-us/answers/questions/1001662/suddenly-getting-securestorage-issues-in-maui
            }
        }
    }
}
