using ActualChat.Security;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Caching;
using ActualLab.Rpc;

namespace ActualChat.App.Maui.Services;

public sealed class MauiSession(IServiceProvider services)
{
    private const string SessionStorageKey = "Fusion.SessionId";
    private static readonly Tracer Tracer = Tracer.Default[nameof(MauiSession)];
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<MauiSession>();

    private static readonly Lock Lock = new();
    // Validate-and-replace can be driven from more than one place, and each call to the server mints
    // its own session - without this two of them race and the resolver and the storage end up split.
    private static readonly SemaphoreSlim ReplaceLock = new(1, 1);
    private static Task<Session?>? _readSessionTask;

    private IServiceProvider Services { get; } = services;
    private TrueSessionResolver TrueSessionResolver { get; } = services.GetRequiredService<TrueSessionResolver>();
    private IMobileSessions MobileSessions => field ??= Services.GetRequiredService<IMobileSessions>();
    private KvasarRemoteComputedCache RemoteComputedCache
        => field ??= Services.GetRequiredService<KvasarRemoteComputedCache>();

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

            // The stored session is used as-is, without asking the server whether it's still valid:
            // that call can't be answered offline, and AccountUI replaces the session once the
            // server does tell us it's unusable. Startup shouldn't wait on either.
            var session = await ReadStored().ConfigureAwait(false);
            if (session == null) {
                session = await MobileSessions
                    .CreateSession(MauiSettings.AppUserAgent, CancellationToken.None)
                    .ConfigureAwait(false);
                _ = Task.Run(() => Store(session));
            }

            TrueSessionResolver.Session = session;
            // No InvalidateEverything: Acquire returns early once a session is set, so nothing
            // session-scoped has been computed yet - only the cache needs pointing.
            await RemoteComputedCache.Activate(session).ConfigureAwait(false);
        });
    }

    public async Task Replace(CancellationToken cancellationToken)
    {
        if (!TrueSessionResolver.HasSession)
            return;

        var invalidSession = TrueSessionResolver.Session;
        await ReplaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            // Someone else replaced it while we were waiting for the lock
            if (TrueSessionResolver.Session != invalidSession)
                return;

            var session = await MobileSessions
                .CreateSession(MauiSettings.AppUserAgent, cancellationToken)
                .ConfigureAwait(false);
            // Switch first: it re-points the cache, so a crash before Store leaves the old id with a
            // cold cache. The other order can persist the new id over the old session's entries.
            await SwitchTo(session).ConfigureAwait(false);
            await Store(session).ConfigureAwait(false);
            Log.LogInformation("Replaced an invalid Session");
        }
        finally {
            ReplaceLock.Release();
        }
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

    private async Task SwitchTo(Session session)
    {
        lock (Lock)
            _readSessionTask = Task.FromResult<Session?>(session);
        TrueSessionResolver.Replace(session);
        // Replace starts the disconnect but doesn't wait for it, and it's the reconnect that carries
        // the new session id in the connection header. A call that beats it rides the old connection,
        // so the server resolves Session.Default to the session we just replaced - a deactivated one,
        // whose guest account never changes again no matter what the app signs in.
        try {
            var peer = Services.RpcHub().GetClientPeer(RpcRef.Default);
            await peer.Disconnect().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to await the RPC disconnect after a Session switch");
        }
        await OnSessionChanged(session).ConfigureAwait(false);
    }

    private async Task OnSessionChanged(Session session)
    {
        // Every client call passes Session.Default, so the session id is never part of a computed's
        // key - the server resolves it per connection. A change thus changes no key and triggers no
        // invalidation, and the reconnect only resets cached values when the server peer itself
        // changed. So both caches have to be re-pointed by hand: the persistent one at the folder
        // belonging to this session, the in-memory one by dropping every value it holds.
        await RemoteComputedCache.Activate(session).ConfigureAwait(false);
        ComputedRegistry.InvalidateEverything();
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
