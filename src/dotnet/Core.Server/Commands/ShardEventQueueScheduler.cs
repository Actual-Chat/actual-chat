using ActualChat.Hosting;

namespace ActualChat.Commands;

public class ShardEventQueueScheduler(HostRole hostRole, IServiceProvider services)
    : ShardWorker<ShardScheme.EventQueue>(services, $"{hostRole}-EventQueueScheduler")
{
    public sealed record Options
    {
        public int Concurrency { get; set; } = HardwareInfo.GetProcessorCountFactor(8);
    }

    private static readonly ConcurrentDictionary<Type, Func<CommandHandler, IReadOnlySet<HostRole>>>
        _hostRoleResolverCache = new ();

    private static readonly PropertyInfo ChainIdSetterProperty =
        typeof(IEventCommand).GetProperty(nameof(IEventCommand.ChainId))!;

    private Action<IEventCommand, Symbol>? _chainIdSetter;

    private HostRole HostRole { get; } = hostRole;

    private Options Settings { get; } = services.GetKeyedService<Options>(hostRole.Id.Value)
        ?? services.GetRequiredService<Options>();

    private ICommandQueues Queues { get; } = services.GetRequiredService<ICommandQueues>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();
    private CommandHandlerResolver HandlerResolver { get; } = services.GetRequiredService<CommandHandlerResolver>();
    private Action<IEventCommand, Symbol> ChainIdSetter => _chainIdSetter ??= ChainIdSetterProperty.GetSetter<Symbol>();

    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var queueId = new QueueId(HostRole.EventQueue, shardIndex);
        var queueBackend = (IEventQueueBackend)Queues.GetBackend(queueId);
        var parallelOptions = new ParallelOptions {
            MaxDegreeOfParallelism = Settings.Concurrency,
            CancellationToken = cancellationToken,
        };
        var events = queueBackend.Read(HostRole, cancellationToken);
        return Parallel.ForEachAsync(events, parallelOptions, (c, ct) => HandleEvent(queueBackend, c, ct));
    }

    private async ValueTask HandleEvent(
        IEventQueueBackend queueBackend,
        QueuedCommand command,
        CancellationToken cancellationToken)
    {
        Log.LogDebug("Running queued command: {Command}", command);
        if (command.UntypedCommand is not IEventCommand untypedCommand) {
            Log.LogWarning("Unable to handle a command as an event: {Event}", command.UntypedCommand);
            return;
        }

        var context = CommandContext.New(Commander, untypedCommand, true);
        Exception? error = null;
        try {

            var handlers = HandlerResolver.GetCommandHandlers(untypedCommand);
            var handlerChains = handlers.HandlerChains;
            if (handlerChains.Count == 0) {
                await OnUnhandledEvent(untypedCommand, cancellationToken).ConfigureAwait(false);
                return;
            }

            var filteredHandlerChains = handlerChains
                .Where(c => GetHandlerChainHostRoles(c.Value).Contains(HostRole))
                .ToList();

            if (filteredHandlerChains.Count == 0)
                return; // An event will be handled by other host roles

            var callTasks = new Task[handlerChains.Count];
            var i = 0;
            foreach (var (chainId, _) in handlerChains) {
                var chainCommand = MemberwiseCloner.Invoke(untypedCommand);
                ChainIdSetter.Invoke(chainCommand, chainId);
                callTasks[i++] = Commander.Call(chainCommand, context.IsOutermost, cancellationToken);
            }
            await Task.WhenAll(callTasks).ConfigureAwait(false);
        }
        catch (Exception e) {
            error = e;
            context.SetResult(e);

            Log.LogError(e, "Running queued command failed: {Command}", command);
            await queueBackend.MarkFailed(command, e, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;
        }
        finally {
            if (error == null)
                await queueBackend.MarkCompleted(command, cancellationToken).ConfigureAwait(false);
            context.TryComplete(cancellationToken);
            await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected virtual ValueTask OnUnhandledEvent(
        IEventCommand command,
        CancellationToken cancellationToken)
    {
        Log.LogWarning("Unhandled event: {Event}", command);
        return ValueTask.CompletedTask;
    }

    private IReadOnlySet<HostRole> GetHandlerChainHostRoles(ImmutableArray<CommandHandler> handlerChain)
    {
        var finalHandler = handlerChain.Single(h => !h.IsFilter);
        var hostRoleProvider = _hostRoleResolverCache.GetOrAdd(finalHandler.GetType(),
            static type => {
                if (!type.IsGenericType)
                    throw StandardError.NotSupported(type, "Unsupported command handler.");

                Func<CommandHandler, Type> serviceTypeGetter;
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(MethodCommandHandler<>))
                    serviceTypeGetter = type.GetProperty("ServiceType")!.GetGetter<CommandHandler, Type>();
                else if (genericTypeDefinition == typeof(ActualLab.CommandR.Configuration.InterfaceCommandHandler<>))
                    serviceTypeGetter = type.GetProperty("ServiceType")!.GetGetter<CommandHandler, Type>();
                else
                    throw StandardError.NotSupported(type, "Unsupported command handler.");

                return ch => HostRoles.GetServedByRoles(serviceTypeGetter(ch));
            });

       return hostRoleProvider(finalHandler);
    }
}
