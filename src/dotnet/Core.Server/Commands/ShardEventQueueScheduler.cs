using ActualChat.Concurrency;
using ActualChat.Hosting;

namespace ActualChat.Commands;

public sealed class ShardEventQueueScheduler(HostRole hostRole, IServiceProvider services)
    : ShardWorker<ShardScheme.EventQueue>(services, $"{hostRole}-EventQueueScheduler"), ICommandQueueScheduler
{
    public sealed record Options
    {
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
    }

    private static readonly PropertyInfo ChainIdSetterProperty =
        typeof(IEventCommand).GetProperty(nameof(IEventCommand.ChainId))!;

    private ILogger? _log;
    private Action<IEventCommand, Symbol>? _chainIdSetter;
    private long _lastCommandTicks = 0;

    private Options Settings { get; } = services.GetKeyedService<Options>(hostRole.Id.Value)
        ?? services.GetRequiredService<Options>();

    private HostRole HostRole { get; } = hostRole;
    private ICommandQueues Queues { get; } = services.GetRequiredService<ICommandQueues>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private EventHandlerResolver EventHandlerResolver { get; } = services.GetRequiredService<EventHandlerResolver>();
    private Action<IEventCommand, Symbol> ChainIdSetter => _chainIdSetter ??= ChainIdSetterProperty.GetSetter<Symbol>();

    protected override ILogger Log => _log ??= Services.Logs().CreateLogger($"{GetType()}({HostRole})");

    public async Task ProcessAlreadyQueued(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (true) {
            await Clock.Delay(timeout, cancellationToken).ConfigureAwait(false);

            var lastCommandTicks = Interlocked.Read(ref _lastCommandTicks);
            var currentTicks = Clock.UtcNow.Ticks;
            if (currentTicks - lastCommandTicks > timeout.Ticks)
                break;
        }
    }

    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var queueId = new QueueId(HostRole.EventQueue, shardIndex);
        var queueBackend = (IEventQueueBackend)Queues.GetBackend(queueId);

        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        var eventHandlers = EventHandlerResolver.GetEventHandlers(HostRole);
        if (eventHandlers.Length == 0) {
            // there is no event handlers implemented for the host role
            Log.LogInformation("Stopping event scheduler - there are no handlers implemented for the {HostRole}", HostRole);
            _ = Stop();
            return Task.CompletedTask;
        }

        return eventHandlers
            .Select(h => new AsyncChain($"{HostRole}.{nameof(HandleEvents)}({h.Id})", ct => HandleEvents(h, queueBackend, ct)))
            .Select(ac => ac
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log))
            .RunIsolated(cancellationToken);
    }

    private Task HandleEvents(
        CommandHandler handler,
        IEventQueueBackend queueBackend,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.Concurrency,
            CancellationToken = cancellationToken,
        };

        var consumerPrefix = string.Join("-",
            handler.Id.Value
                .Split('.')
                .TakeLast(2));
        var events = queueBackend.Read(consumerPrefix, handler.CommandType, cancellationToken);
        return Parallel.ForEachAsync(
            events,
            parallelOptions,
            (c, ct) => HandleEvent(handler, queueBackend, consumerPrefix, c, ct));
    }

    private async ValueTask HandleEvent(
        CommandHandler handler,
        IEventQueueBackend queueBackend,
        string consumerPrefix,
        QueuedCommand command,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("Running queued event with {ConsumerPrefix}: {Command}", consumerPrefix, command.UntypedCommand);
        if (command.UntypedCommand is not IEventCommand untypedCommand) {
            Log.LogWarning("Unable to handle a command as an event: {Event}", command.UntypedCommand);
            return;
        }

        var context = CommandContext.New(Commander, untypedCommand, true);
        Exception? error = null;
        try {
            var chainId = handler.Id;
            var chainCommand = MemberwiseCloner.Invoke(untypedCommand);
            ChainIdSetter.Invoke(chainCommand, chainId);
            await Commander.Call(chainCommand, context.IsOutermost, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Complete handling event with {ConsumerPrefix}: {Command}", consumerPrefix, command.UntypedCommand);
        }
        catch (Exception e) {
            error = e;
            context.SetResult(e);

            Log.LogError(e, "Running queued event  with {ConsumerPrefix} failed: {Command}", consumerPrefix, command.UntypedCommand);
            await queueBackend.MarkFailed(consumerPrefix, command, e, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        finally {
            var commandCompletionTicks = Clock.UtcNow.Ticks;
            InterlockedExt.ExchangeIfGreaterThan(ref _lastCommandTicks, commandCompletionTicks);

            if (error == null)
                await queueBackend.MarkCompleted(consumerPrefix, command, cancellationToken).ConfigureAwait(false);
            context.TryComplete(cancellationToken);
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}
