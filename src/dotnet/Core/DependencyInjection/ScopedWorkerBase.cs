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
        lock (Lock) {
            if (_whenRunning != null)
                return _whenRunning;

            _whenRunning = Task.Run(() => OnRun(StopToken), StopToken);
        }
        Hub.RegisterAwaitable(_whenRunning);
        return _whenRunning;
    }

    protected abstract Task OnRun(CancellationToken cancellationToken);
}
