namespace ActualChat.UI.Blazor;

#pragma warning disable MA0064

public interface IUIWorker
{
    Task Run();
}

public abstract class UIWorkerBase<THub>(THub hub) : UIServiceBase<THub>(hub), IUIWorker
    where THub : UIHub
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
                catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
                    // WhenRunning still never throws - same as WorkerBase - and the worker still
                    // doesn't restart; per-worker retry stays each worker's own business. But a
                    // worker that dies here is dead for the rest of the process, so it says so.
                    Log.LogError(e, "{Worker} failed and stopped", GetType().GetName());
                }
                catch {
                    // Cancelled on shutdown
                }
            }, CancellationToken.None);
        }
        Hub.RegisterAwaitable(_whenRunning);
        return _whenRunning;
    }

    protected abstract Task OnRun(CancellationToken cancellationToken);
}
