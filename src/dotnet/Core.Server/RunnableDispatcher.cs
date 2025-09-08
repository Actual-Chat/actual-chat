using ActualLab.Internal;

namespace ActualChat;

public sealed class RunnableDispatcher : ProcessorBase
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

    // Use

    public IAsyncDisposable Use(IRunnable runnable)
        => new RunnableScope(this, runnable);

    public IAsyncDisposable Use(IRunnableRunner runner)
        => new RunnableRunnerScope(this, runner);

    // Add

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

    // Remove

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

    // Nested types

    public sealed class RunnableScope : IAsyncDisposable
    {
        private readonly RunnableDispatcher? _scheduler;
        private readonly IRunnable? _runnable;

        public RunnableScope(RunnableDispatcher scheduler, IRunnable runnable)
        {
            if (!scheduler.Add(runnable))
                return;

            _scheduler = scheduler;
            _runnable = runnable;
        }

        public ValueTask DisposeAsync()
            => _scheduler is null || _runnable is null
                ? default
                : _scheduler.Remove(_runnable);
    }

    public sealed class RunnableRunnerScope : IAsyncDisposable
    {
        private readonly RunnableDispatcher? _scheduler;
        private readonly IRunnableRunner? _runner;

        public RunnableRunnerScope(RunnableDispatcher scheduler, IRunnableRunner runner)
        {
            if (!scheduler.Add(runner))
                return;

            _scheduler = scheduler;
            _runner = runner;
        }

        public ValueTask DisposeAsync()
            => _scheduler is null || _runner is null
                ? default
                : _scheduler.Remove(_runner);
    }
}
