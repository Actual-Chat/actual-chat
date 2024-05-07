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
    private TaskCompletionSource _entranceGate = new TaskCompletionSource();
    private TaskCompletionSource _exitGate = new TaskCompletionSource();
    private Task _initTask;
    private CancellationTokenSource _restartTrigger;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        _restartTrigger = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _initTask = InitializeAsync(cancellationToken);
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

            // Open entrance gate
            _entranceGate.SetResult();
        }
        catch (Exception e) {
            _entranceGate.SetException(e);
        }
    }

    public async Task ExecuteWhenReadyAsync(Func<CancellationToken, Task> asyncAction, CancellationToken actionCancellationToken)
    {
        using var cancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(_restartTrigger.Token, actionCancellationToken);
        var cancellationToken = cancellationSource.Token;

        await Volatile.Read(ref _entranceGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await asyncAction(cancellationToken).ConfigureAwait(false);
        // TODO: handle critical errors
        // TODO: trigger completion for all
        // Wait all cancelled / completed
        // Open exit gate

        await Volatile.Read(ref _exitGate).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
