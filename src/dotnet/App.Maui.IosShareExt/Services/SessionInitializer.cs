using ActualChat.Maui;
using ActualChat.Security;
using ActualLab.Interception;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class SessionInitializer(TrueSessionResolver trueSessionResolver, ILogger<SessionInitializer> log)
    : WorkerBase, IComputeService, INotifyInitialized
{
    void INotifyInitialized.Initialized()
        => this.Start();

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(SetSession)
            .LogError(log)
            .RetryForever(RetryDelaySeq.Fixed(1))
            .RunIsolated(cancellationToken);

    private async Task SetSession(CancellationToken cancellationToken)
    {
        var sessionId = await IosSharedSecureStorage.Default.GetAsync("Fusion.SessionId")
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (sessionId.IsNullOrEmpty()) {
            log.LogCritical("No session id found.");
            return;
        }
        trueSessionResolver.Session = new Session(sessionId);
    }
}
