using ActualChat.Maui;
using ActualChat.Security;
using ActualLab.Interception;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class SessionInitializer(TrueSessionResolver trueSessionResolver, ILogger<SessionInitializer> log)
    : WorkerBase, IComputeService, INotifyInitialized
{
    private const string SessionStorageKey = "Fusion.SessionId";

    void INotifyInitialized.Initialized()
        => this.Start();

    public async Task Refresh(CancellationToken cancellationToken)
    {
        try {
            await SetSession(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            log.LogError(e, "Failed to refresh the session");
        }
    }

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SetSession)
            .Log(LogLevel.Debug, log)
            .RetryForever(RetryDelaySeq.Fixed(1), log)
            .RunIsolated(cancellationToken);

    // Private methods

    private async Task SetSession(CancellationToken cancellationToken)
    {
        var sessionId = await AppleSharedSecureStorage.Default.GetAsync(SessionStorageKey)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sessionId.IsNullOrEmpty()) {
            log.LogWarning("No session id found");
            return;
        }

        var session = new Session(sessionId);
        var oldSession = trueSessionResolver.HasSession ? trueSessionResolver.Session : null;
        if (oldSession == session)
            return;

        // Replace rather than the Session setter: the setter throws AlreadyInitialized once the
        // main app rotates the stored id, and this process outlives many shares.
        trueSessionResolver.Replace(session);
        if (oldSession is null)
            return;

        // Everything computed for the previous session - the own account above all - survives the
        // reconnect Replace triggers, so without this the sheet keeps showing the previous user.
        log.LogInformation("Session changed - invalidating everything");
        ComputedRegistry.InvalidateEverything();
    }
}
