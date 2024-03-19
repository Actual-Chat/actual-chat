using ActualChat.Attributes;
using ActualLab.CommandR.Internal;

namespace ActualChat.Queues.Internal;

public sealed class QueueRefResolver(IServiceProvider services) : IQueueRefResolver
{
    private static readonly ConcurrentDictionary<Type, QueueAttribute?> _queueAttributes = new();

    private BackendServiceDefs BackendServiceDefs { get; } = services.GetRequiredService<BackendServiceDefs>();
    private CommandHandlerResolver CommandHandlerResolver { get; } = services.GetRequiredService<CommandHandlerResolver>();

    public QueueShardRef GetQueueShardRef(ICommand command)
    {
        var queueRef = GetQueueRef(command);
        var shardKeyResolver = ShardKeyResolvers.GetUntyped(command.GetType()) ?? ShardKeyResolvers.DefaultResolver;
        var shardKey = shardKeyResolver.Invoke(command);
        return new QueueShardRef(queueRef, shardKey);
    }

    public QueueRef GetQueueRef(ICommand command)
    {
        var commandType = command.GetType();
        var commandKind = command.GetKind();

        // 1. Try use [Queue] attribute
        var queueRef = ResolveAttribute(commandType);
        if (!queueRef.IsUndefined)
            return queueRef;

        // 2. All events go to EventQueue by default
        if (commandKind is CommandKind.UnboundEvent)
            return ShardScheme.EventQueue;

        // 3. Everything else goes to the queue matching command handler's backend
        var handlers = CommandHandlerResolver.GetCommandHandlers(commandType);
        var handlerChain = handlers.SingleHandlerChain;
        if (commandKind is CommandKind.BoundEvent) {
            var eventCommand = (IEventCommand)command;
            handlerChain = handlers.HandlerChains[eventCommand.ChainId];
        }

        var finalHandler = (CommandHandler?)null;
        for (var i = handlerChain.Length - 1; i >= 0; i--) {
            var handler = handlerChain[i];
            if (!handler.IsFilter) {
                finalHandler = handler;
                break;
            }
        }
        if (finalHandler == null)
            throw Errors.NoFinalHandlerFound(commandType);

        var serviceType = finalHandler.GetServiceType();
        if (serviceType == null)
            throw StandardError.Internal($"Unsupported command handler type: {finalHandler.GetType().GetName()}.");

        var serviceDef = BackendServiceDefs[serviceType];
        return serviceDef.ShardScheme;
    }

    public static QueueRef ResolveAttribute(Type type)
    {
        var attr = _queueAttributes.GetOrAdd(type,
            static (_, t) => t
                .GetCustomAttributes<QueueAttribute>()
                .SingleOrDefault(),
            type);
        return attr != null
            ? ShardScheme.ById[attr.ShardScheme]
            : QueueRef.Undefined;
    }
}
