namespace ActualChat.Commands;

public class EventCommander : ICommander
{
    private readonly ConcurrentLruCache<ICommand, ICommand> _duplicateCache = new (128);

    private ILogger Log { get; init; }
    private IEventHandlerResolver EventHandlerResolver { get; }

    public CommanderOptions Options { get; }
    public IServiceProvider Services { get; }

    public EventCommander(CommanderOptions options, IServiceProvider services)
    {
        Options = options;
        Services = services;
        Log = Services.LogFor(GetType());
        EventHandlerResolver = services.GetRequiredService<IEventHandlerResolver>();
    }

    public Task Run(CommandContext context, CancellationToken cancellationToken = default)
    {
        var command = context.UntypedCommand;

        // skip duplicates
        if (!_duplicateCache.TryAdd(command, command)) {
            context.TryComplete(cancellationToken);
            return context.DisposeSilentlyAsync().AsTask();
        }

        // Task.Run is used to call RunInternal to make sure parent
        // task's ExecutionContext won't be "polluted" by temp.
        // change of CommandContext.Current (via AsyncLocal).
        using var _ = context.IsOutermost ? ExecutionContextExt.SuppressFlow() : default;
        var handlerBatches = EventHandlerResolver.GetEventHandlers(command.GetType());
        if (handlerBatches.Count <= 1) {
            context.ExecutionState = new CommandExecutionState(handlerBatches.FirstOrDefault(Array.Empty<CommandHandler>()));
            return Task.Run(() => RunInternal(context, cancellationToken), default);
        }
        var tasks = new Task[handlerBatches.Count + 1];
        for (int i = 0; i < handlerBatches.Count; i++) {
            var handlerBatch = handlerBatches[i];
            var handlerContext =  CommandContext.New(this, command, true);
            handlerContext.ExecutionState = new CommandExecutionState(handlerBatch);
            tasks[i] = Task.Run(() => RunInternal(handlerContext, cancellationToken), default);
        }
        context.TryComplete(cancellationToken);
        tasks[^1] = context.DisposeSilentlyAsync().AsTask();
        return Task.WhenAll(tasks);
    }

    protected virtual async Task RunInternal(
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        try {
            var command = context.UntypedCommand;
            if (context.ExecutionState.Handlers.Count == 0)
                await OnUnhandledCommand(command, context, cancellationToken).ConfigureAwait(false);

            using var _ = context.Activate();
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Error running event handler");
            context.SetResult(e);
        }
        finally {
            context.TryComplete(cancellationToken);
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected virtual Task OnUnhandledCommand(
        ICommand command,
        CommandContext context,
        CancellationToken cancellationToken)
        => throw Errors.NoHandlerFound(command.GetType());
}
