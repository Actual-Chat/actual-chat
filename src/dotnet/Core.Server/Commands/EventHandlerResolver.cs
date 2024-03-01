using ActualChat.Hosting;

namespace ActualChat.Commands;

public sealed class EventHandlerResolver(IServiceProvider services) : CommandHandlerResolver(services)
{
    private static readonly ConcurrentDictionary<Type, Func<CommandHandler, IReadOnlySet<HostRole>, bool>>
        _hostRoleResolverCache = new ();

    private CommandHandlerResolver Resolver { get; } = services.GetRequiredService<CommandHandlerResolver>();

    public ImmutableArray<CommandHandler> GetEventHandlers(HostRole hostRole)
        => Registry.Handlers
            .Where(h => !h.IsFilter
                && typeof(IEventCommand).IsAssignableFrom(h.CommandType)
                && Filter.Invoke(h, h.CommandType))
            .Where(h => ShouldServe(h, HostRoles.Server.GetAllRoles(hostRole)))
            .ToImmutableArray();


    public override CommandHandlerSet GetCommandHandlers(Type commandType)
        => Resolver.GetCommandHandlers(commandType);

    private static bool ShouldServe(CommandHandler handler, IReadOnlySet<HostRole> hostRoles)
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

                return (ch, hrs) => hrs.ShouldServe(serviceTypeGetter(ch));
            });

        return hostRoleProvider(handler, hostRoles);
    }
}
