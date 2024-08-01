using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.Module;

internal interface IServiceCoordinator
{
    Task ExecuteWhenReadyAsync(Func<CancellationToken, Task> asyncAction, CancellationToken actionCancellationToken);
    Task<TResult> ExecuteWhenReadyAsync<TResult>(Func<CancellationToken, Task<TResult>> asyncFunc, CancellationToken funcCancellationToken);
}

internal sealed class ServiceCoordinator(
    IClusterSetup clusterSetup,
    IMomentClock clock,
    ILogger<ServiceCoordinator> log
) : WorkerBase, IServiceCoordinator
{
    private TaskCompletionSource _entranceGate = new();

    public RetryDelaySeq RetryDelaySeq { get; init; } = RetryDelaySeq.Exp(0.5, 60);
    public TransiencyResolver TransiencyResolver { get; init; } = TransiencyResolvers.PreferTransient;
    public Task OnStartTask { get; init; } = Task.CompletedTask;

    protected override Task OnStart(CancellationToken cancellationToken) => OnStartTask;
    protected override async Task OnRun(CancellationToken cancellationToken)
        => await InitializeAsync(cancellationToken).ConfigureAwait(false);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                await AsyncChain.From(clusterSetup.InitializeAsync)
                    .WithTransiencyResolver(TransiencyResolver)
                    .Log(LogLevel.Debug, log)
                    .RetryForever(RetryDelaySeq, log)
                    .Run(cancellationToken)
                    .ConfigureAwait(false);

                // Open Search cluster is initialized so open entrance gate
                _entranceGate.SetResult();
                break;
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                var transiency = TransiencyResolver(e);
                if (transiency.IsTerminal()) {
                    log.LogError(e, "[!] Irrecoverable error detected, exiting initialization.");
                    throw;
                }
                // While with high probability the error is irrecoverable
                // whe retry initialization to show activity in logs.
                log.LogError(e, "[!] Critical AsyncChain pipeline error, will retry in 30s.");
                await clock.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ExecuteWhenReadyAsync(Func<CancellationToken, Task> asyncAction, CancellationToken cancellationToken)
    {
        await Volatile.Read(ref _entranceGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await asyncAction(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteWhenReadyAsync<TResult>(Func<CancellationToken, Task<TResult>> asyncFunc, CancellationToken cancellationToken)
    {
        await Volatile.Read(ref _entranceGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await asyncFunc(cancellationToken).ConfigureAwait(false);
    }
}
