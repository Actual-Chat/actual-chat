using ActualChat.Hosting;

namespace ActualChat.Commands;

public sealed class EventHandlerResolver(IServiceProvider services) : CommandHandlerResolver(services)
{
    private static readonly ConcurrentDictionary<Type, Func<CommandHandler, IReadOnlySet<HostRole>>>
        _hostRoleResolverCache = new ();

    private CommandHandlerResolver Resolver { get; } = services.GetRequiredService<CommandHandlerResolver>();

    public ImmutableArray<CommandHandler> GetEventHandlers(HostRole hostRole)
        => Registry.Handlers
            .Where(h => !h.IsFilter
                && typeof(IEventCommand).IsAssignableFrom(h.CommandType)
                && Filter.Invoke(h, h.CommandType))
            .Where(h => GetHandlerChainHostRoles(h).Contains(hostRole))
            .ToImmutableArray();


    public override CommandHandlerSet GetCommandHandlers(Type commandType)
        => Resolver.GetCommandHandlers(commandType);

    public IReadOnlySet<HostRole> GetHandlerChainHostRoles(CommandHandler handler)
    {
        var hostRoleProvider = _hostRoleResolverCache.GetOrAdd(handler.GetType(),
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

        return hostRoleProvider(handler);
    }
}
