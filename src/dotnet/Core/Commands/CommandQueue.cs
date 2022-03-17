namespace ActualChat.Commands;
using Microsoft.Extensions.Hosting;

public class CommandQueue<TQueueCommand> : IAsyncDisposable
    where TQueueCommand : IQueueCommand
{
    private readonly ICommandProcessor<TQueueCommand> _commandProcessor;
    private readonly CancellationToken _applicationStopping;
    private readonly Channel<CommandExecution> _commands;
    private int _isDisposed;
    private Task? _commandLoopTask;
    private CancellationTokenSource? _commandLoopCts;
    private readonly object _runLocker = new();

    private static Channel<CommandExecution> CreateChannel()
        => Channel.CreateBounded<CommandExecution>(
            new BoundedChannelOptions(128) {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

    public CommandQueue(
        IHostApplicationLifetime lifetime,
        ICommandProcessor<TQueueCommand> commandProcessor)
        :this(lifetime, commandProcessor, CreateChannel())
    {
    }

    public CommandQueue(
        IHostApplicationLifetime lifetime,
        ICommandProcessor<TQueueCommand> commandProcessor,
        Channel<CommandExecution> commands)
    {
        _applicationStopping = lifetime.ApplicationStopping;
        _commandProcessor = commandProcessor;
        _commands = commands;
    }

    public async ValueTask<CommandExecution> EnqueueCommand(TQueueCommand command, CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        if (_isDisposed == 1)
            throw new LifetimeException("CommandQueue is disposed.", new ObjectDisposedException(nameof(CommandQueue<TQueueCommand>)));
        RunCommandLoopIfNeeded();
        CommandExecution execution = new(command);
        await _commands.Writer.WriteAsync(execution, cancellationToken).ConfigureAwait(false);
        return execution;
    }

    private void RunCommandLoopIfNeeded()
    {
        if (_commandLoopTask != null)
            return;
        lock (_runLocker) {
            if (_commandLoopTask != null)
                return;

            if (_isDisposed == 1)
                throw new LifetimeException("CommandQueue is disposed.", new ObjectDisposedException(nameof(CommandQueue<TQueueCommand>)));

            _commandLoopCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
            _commandLoopTask = CommandLoop(_commandLoopCts.Token)
                .ContinueWith(task => {
                    lock (_runLocker) {
                        _commandLoopCts?.Dispose();
                        _commandLoopCts = null;
                        _commandLoopTask = null;
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task CommandLoop(CancellationToken cancellationToken)
    {
        await _commandProcessor.OnCommandLoopStarted(cancellationToken).ConfigureAwait(false);
        try {
            var commands = _commands.Reader.ReadAllAsync(cancellationToken);
            await foreach (var execution in commands.ConfigureAwait(false).WithCancellation(cancellationToken)) {
                try {
                    var command = (TQueueCommand)execution.Command;
                    // if a Play call was cancelled we should skip all enqueued commands from this call
                    // note, that stop commands don't use the cancellation token at all
                    if (command.CancellationToken.IsCancellationRequested)
                        continue;
                    await _commandProcessor.ProcessCommand(command, cancellationToken).ConfigureAwait(false);
                    // notify that the command processing is done
                    execution._whenCommandProcessed.TrySetResult();
                }
                catch (Exception ex) {
                    execution._whenCommandProcessed.TrySetException(ex);
                    throw;
                }
            }
        }
        finally {
            await _commandProcessor.OnCommandLoopCompeted(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeAsyncCore()
    {
        var commandLoopTask = Interlocked.Exchange(ref _commandLoopTask, null);
        if (commandLoopTask != null) {
            // cancel the command loop enumeration
            // -> cancel + await running tasks of players
            // -> send StopCommand
            // -> await js callback
            _commandLoopCts?.Cancel();
            try {
                await commandLoopTask.ConfigureAwait(false);
            }
            catch { }
            finally {
                _commands.Writer.TryComplete();
            }
        }
        /// <see cref="_commandLoopCts"/> will be disposed by the continuation
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 1)
            return;
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
