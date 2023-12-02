namespace ActualChat.DependencyInjection;

#pragma warning disable MA0064

public abstract class ScopedWorkerBase(Scope scope)
    : ScopedServiceBase(scope)
{
    private volatile Task? _whenRunning;

    protected readonly object Lock = new();
    protected CancellationToken StopToken => scope.StopToken;
    public Task? WhenRunning => _whenRunning;

    protected ScopedWorkerBase(IServiceProvider services)
        : this(services.Scope()) { }

    public virtual Task Run()
    {
        lock (Lock) {
            if (_whenRunning != null)
                return _whenRunning;

            _whenRunning = Task.Run(() => OnRun(StopToken), StopToken);
        }
        Scope.Register(_whenRunning);
        return _whenRunning;
    }

    protected abstract Task OnRun(CancellationToken cancellationToken);
}
