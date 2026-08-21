using ActualChat.Maui;
using ActualChat.Security;
using ActualChat.UI.Caching;
using ActualLab.Interception;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class SessionInitializer(
    TrueSessionResolver trueSessionResolver,
    KvasarRemoteComputedCache remoteComputedCache,
    IServiceProvider services)
    : WorkerBase, IComputeService, INotifyInitialized
{
    private const string SessionStorageKey = "Fusion.SessionId";

    private IServiceProvider Services { get; } = services;
    private TrueSessionResolver TrueSessionResolver { get; } = trueSessionResolver;
    private KvasarRemoteComputedCache RemoteComputedCache { get; } = remoteComputedCache;
    private ILogger Log => field ??= Services.LogFor(GetType());

    void INotifyInitialized.Initialized()
        => this.Start();

    public async Task Refresh(CancellationToken cancellationToken)
    {
        try {
            await SetSession(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "Failed to refresh the session");
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SetSession)
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Fixed(1), Log)
            .RunIsolated(cancellationToken);

    // Private methods

    private async Task SetSession(CancellationToken cancellationToken)
    {
        var sessionId = await AppleSharedSecureStorage.Default.GetAsync(SessionStorageKey)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sessionId.IsNullOrEmpty()) {
            Log.LogWarning("No session id found");
            return;
        }

        var session = new Session(sessionId);
        var oldSession = TrueSessionResolver.HasSession ? TrueSessionResolver.Session : null;
        if (oldSession == session)
            return;

        // Replace rather than the Session setter: the setter throws AlreadyInitialized once the
        // main app rotates the stored id, and this process outlives many shares.
        TrueSessionResolver.Replace(session);
        // The extension has its own data container - no App Group, only a shared keychain group -
        // so this cache is private to it and can't contend with the main app's.
        await RemoteComputedCache.Activate(session).ConfigureAwait(false);
        if (oldSession is null)
            return;

        // Everything computed for the previous session - the own account above all - survives the
        // reconnect Replace triggers, so without this the sheet keeps showing the previous user.
        Log.LogInformation("Session changed - invalidating everything");
        ComputedRegistry.InvalidateEverything();
    }
}
