using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.Module;

internal interface IServiceCoordinator
{
    Task ExecuteWhenReadyAsync(Func<CancellationToken, Task> asyncAction, CancellationToken actionCancellationToken);
}

internal class ServiceCoordinator(
    IClusterSetup clusterSetup,
    ILogger<ServiceCoordinator> log
) : WorkerBase, IServiceCoordinator
{
    private TaskCompletionSource _entranceGate = new();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ActualLab.Async.TaskExt.NewNeverEndingUnreferenced()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0, 60);

        try {
            await AsyncChain.From(clusterSetup.InitializeAsync)
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, log)
                .RetryForever(retryDelays, log)
                .Run(cancellationToken)
                .ConfigureAwait(false);

            // Open Search cluster is initialized so open entrance gate
            _entranceGate.SetResult();
        }
        catch (Exception e) {
            var oldGate = Interlocked.Exchange(ref _entranceGate, new TaskCompletionSource());
            oldGate.SetException(e);
            throw;
        }
    }

    public async Task ExecuteWhenReadyAsync(Func<CancellationToken, Task> asyncAction, CancellationToken cancellationToken)
    {
        await Volatile.Read(ref _entranceGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await asyncAction(cancellationToken).ConfigureAwait(false);
    }
}
