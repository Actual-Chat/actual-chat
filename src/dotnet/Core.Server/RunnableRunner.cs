using ActualLab.Internal;

namespace ActualChat;

public interface IRunnableRunner : IAsyncDisposable, IDisposable, IHasWhenDisposed
{
    ImmutableDictionary<IRunnable, StartedRunnable> StartedRunnables { get; }
    bool Start(IRunnable runnable, out StartedRunnable started);
}

public class RunnableRunner : ProcessorBase, IRunnableRunner
{
    public ImmutableDictionary<IRunnable, StartedRunnable> StartedRunnables { get; private set; }
        = ImmutableDictionary<IRunnable, StartedRunnable>.Empty;

    protected override Task DisposeAsyncCore()
    {
        Task[] tasks;
        lock (Lock) {
            tasks = new Task[StartedRunnables.Count];
            var index = 0;
            foreach (var (_, startedRunnable) in StartedRunnables) {
                startedRunnable.Stop();
                tasks[index++] = startedRunnable.Task;
            }
        }
        return Task.WhenAll(tasks).SuppressExceptions();
    }

    public bool Start(IRunnable runnable, out StartedRunnable started)
    {
        lock (Lock) {
            var stopToken = StopToken;
            if (stopToken.IsCancellationRequested)
                throw Errors.AlreadyDisposed();

            if (StartedRunnables.TryGetValue(runnable, out started!))
                return false;

            started = new StartedRunnable(
                this, runnable, stopToken,
                static x => {
                    var runner = (RunnableRunner)x.RunnableRunner;
                    if (runner.WhenDisposed is not null) return;
                    lock (runner.Lock) { // Double-check locking
                        if (runner.WhenDisposed is not null) return;

                        runner.StartedRunnables = runner.StartedRunnables.Remove(x.Runnable);
                    }
                });
            StartedRunnables = StartedRunnables.Add(runnable, started);
            return true;
        }
    }
}
