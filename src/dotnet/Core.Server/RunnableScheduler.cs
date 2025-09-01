using ActualLab.Internal;

namespace ActualChat;

public sealed class RunnableScheduler : ProcessorBase
{
    public ImmutableHashSet<IRunnable> Runnables { get; private set; } = ImmutableHashSet<IRunnable>.Empty;
    public ImmutableHashSet<IRunnableRunner> Runners { get; private set; } = ImmutableHashSet<IRunnableRunner>.Empty;

    protected override Task DisposeAsyncCore()
    {
        var tasks = new Task[Runners.Count];
        lock (Lock) {
            var index = 0;
            foreach (var runner in Runners)
                tasks[index++] = runner.DisposeAsync().AsTask();
        }
        return Task.WhenAll(tasks).SuppressExceptions();
    }

    public bool Add(IRunnable runnable)
    {
        lock (Lock) {
            if (WhenDisposed is not null)
                throw Errors.AlreadyDisposed();

            var runnables = Runnables.Add(runnable);
            if (runnables == Runnables)
                return false;

            Runnables = runnables;
            foreach (var runner in Runners)
                runner.Start(runnable, out _);
            return true;
        }
    }

    public bool Add(IRunnableRunner runner)
    {
        lock (Lock) {
            if (WhenDisposed is not null)
                throw Errors.AlreadyDisposed();

            var runners = Runners.Add(runner);
            if (runners == Runners)
                return false;

            Runners = runners;
            foreach (var runnable in Runnables)
                runner.Start(runnable, out _);
            return true;
        }
    }

    public ValueTask Remove(IRunnable runnable, bool mustStop = true)
    {
        var stopTasks = new List<Task>();
        lock (Lock) {
            var runnables = Runnables.Remove(runnable);
            if (runnables == Runnables)
                return default;

            Runnables = runnables;
            if (!mustStop)
                return default;

            foreach (var runner in Runners) {
                var startedRunnable = runner.StartedRunnables.GetValueOrDefault(runnable);
                if (startedRunnable is null)
                    continue;

                startedRunnable.Dispose();
                stopTasks.Add(startedRunnable.Task);
            }
        }
        return Task.WhenAll(stopTasks).SuppressExceptions().ToValueTask();
    }

    public ValueTask Remove(IRunnableRunner runner, bool mustStop = true)
    {
        lock (Lock) {
            var runners = Runners.Remove(runner);
            if (runners == Runners)
                return default;

            Runners = runners;
        }
        return mustStop
            ? runner.DisposeAsync()
            : default;
    }
}
