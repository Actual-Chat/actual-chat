namespace ActualChat.DependencyInjection;

#pragma warning disable MA0064

public interface IScopedWorker
{
    Task Run();
}

public abstract class ScopedWorkerBase<TScope>(TScope hub) : ScopedServiceBase<TScope>(hub), IScopedWorker
    where TScope : Hub
{
    private volatile Task? _whenRunning;

    protected readonly object Lock = new();
    protected CancellationToken StopToken => Hub.StopToken;
    public Task? WhenRunning => _whenRunning;

    public virtual Task Run()
    {
        if (_whenRunning != null)
            return _whenRunning;
        lock (Lock) {
            if (_whenRunning != null)
                return _whenRunning;
            if (StopToken.IsCancellationRequested)
                return _whenRunning = Task.CompletedTask;

            // ReSharper disable once PossibleMultipleWriteAccessInDoubleCheckLocking
            _whenRunning = Task.Run(async () => {
                try {
                    await OnRun(StopToken).ConfigureAwait(false);
                }
                catch {
                    // Intended: WhenRunning should behave similarly
                    // to how it behaves in WorkerBase, i.e. never throw.
                }
            }, CancellationToken.None);
        }
        Hub.RegisterAwaitable(_whenRunning);
        return _whenRunning;
    }

    protected abstract Task OnRun(CancellationToken cancellationToken);
}
