using ActualLab.Internal;

namespace ActualChat.IO;

public class LazyWriter<T> : WorkerBase
{
    private readonly Channel<Command> _commands;

    public int FlushMaxItemCount { get; init; } = 64;
    public TimeSpan FlushDelay { get; init; } = TimeSpan.FromMilliseconds(1);
    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public RetryDelaySeq FlushRetryDelays { get; init; } = new();
    public Func<List<T>, Task> Implementation { get; init; } = _ => Task.CompletedTask;
    public Func<Exception, LogLevel> FlushErrorSeverityProvider { get; init; } = static _ => LogLevel.Error;
    public IMomentClock Clock { get; init; } = MomentClockSet.Default.CpuClock;
    public ILogger? Log { get; init; }

    public LazyWriter()
    {
        _commands = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions() {
            SingleReader = true,
            SingleWriter = false,
        });
        this.Start();
    }

    public void Add(T item)
    {
        var command = new ItemCommand(item);
        if (!_commands.Writer.TryWrite(command))
            throw Errors.AlreadyDisposed();
    }

    public Task Flush(CancellationToken cancellationToken = default)
    {
        var command = new FlushCommand();
        if (!_commands.Writer.TryWrite(command))
            throw Errors.AlreadyDisposed();

        return command.WhenFlushedSource.Task.WaitAsync(cancellationToken);
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        using var abortCts = new CancellationTokenSource();
        var abortToken = abortCts.Token;
        await using var delayedAbortRegistration = cancellationToken.Register(() => {
            _commands.Writer.TryWrite(new FlushCommand());
            _commands.Writer.Complete();
            // ReSharper disable once AccessToDisposedClosure
            _ = Task.Delay(DisposeTimeout, CancellationToken.None)
                .ContinueWith(_ => abortCts.CancelAndDisposeSilently(), TaskScheduler.Default);
        }).ConfigureAwait(false);

        await ProcessCommands(abortToken).ConfigureAwait(false);
    }

    private async Task ProcessCommands(CancellationToken cancellationToken)
    {
        var batch = new List<T>();
        CancellationTokenSource? upcomingFlushCts = null;
        try {
            await foreach (var command in _commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                switch (command) {
                case ItemCommand itemCommand:
                    batch.Add(itemCommand.Item);
                    if (batch.Count >= FlushMaxItemCount)
                        await FlushInternal().ConfigureAwait(false);
                    else if (upcomingFlushCts == null) {
                        // Start the task pushing FlushCommand into the queue after FlushDelay
#pragma warning disable CA2000
                        upcomingFlushCts = new CancellationTokenSource();
#pragma warning restore CA2000
                        _ = Task.Delay(FlushDelay, upcomingFlushCts.Token).ContinueWith(t => {
                            if (!t.IsCanceled)
                                _commands.Writer.TryWrite(new FlushCommand());
                        }, TaskScheduler.Default);
                    }
                    break;
                case FlushCommand flushCommand:
                    var flushTask = FlushInternal();
                    _ = flushCommand.WhenFlushedSource.TrySetFromTaskAsync(flushTask, cancellationToken);
                    await flushTask.ConfigureAwait(false);
                    break;
                }
            }
        }
        finally {
            upcomingFlushCts.CancelAndDisposeSilently();
        }
        return;

        async Task<Unit> FlushInternal()
        {
            // ReSharper disable once AccessToModifiedClosure
            upcomingFlushCts?.CancelAndDisposeSilently();
            upcomingFlushCts = null;
            var failedTryCount = 0;
            while (batch.Count > 0) {
                try {
                    await Implementation.Invoke(batch).ConfigureAwait(false);
                    break;
                }
                catch (Exception e) {
                    failedTryCount++;
                    var retryDelay = FlushRetryDelays[failedTryCount];
                    var severity = FlushErrorSeverityProvider.Invoke(e);
                    if (severity != LogLevel.None)
                        Log?.Log(severity, e,
                            "Error #{ErrorCount} while flushing a batch of {ItemCount} items, will retry in {RetryDelay}",
                            failedTryCount, batch.Count, retryDelay.ToShortString());
                    await Clock.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            batch.Clear();
            return default;
        }
    }

    // Nested types

    private abstract record Command;
    private record ItemCommand(T Item) : Command;
    private record FlushCommand(TaskCompletionSource<Unit> WhenFlushedSource) : Command {
        public FlushCommand() : this(TaskCompletionSourceExt.New<Unit>()) { }
    }
}
