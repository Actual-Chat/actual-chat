using ActualChat.Hosting;

namespace ActualChat.Queues.Internal;

public sealed class EventHandlerRegistry(IServiceProvider services)
{
    private static readonly ConcurrentDictionary<CommandHandler, bool>
        _isLocalCache = new ();

    private CommandHandler[]? _allEventHandlers;
    private CommandHandler[]? _localEventHandlers;

    private HostInfo HostInfo { get; } = services.GetRequiredService<HostInfo>();
    private CommandHandlerRegistry CommandHandlerRegistry { get; } = services.GetRequiredService<CommandHandlerRegistry>();
    private CommandHandlerResolver CommandHandlerResolver { get; } = services.GetRequiredService<CommandHandlerResolver>();

    public CommandHandler[] AllEventHandlers => _allEventHandlers ??= ListAllEventHandlers();
    public CommandHandler[] LocalEventHandlers => _localEventHandlers ??= AllEventHandlers.Where(IsLocal).ToArray();

    public bool IsLocal(CommandHandler handler)
        => _isLocalCache.GetOrAdd(handler,
            static (h, self) => {
                var serviceType = h.GetServiceType();
                if (serviceType == null)
                    throw StandardError.Internal($"Unsupported command handler type: {h.GetType().GetName()}.");

                var hostRoles = self.HostInfo.Roles;
                var backendServiceMode = hostRoles.GetBackendServiceMode(serviceType);
                return backendServiceMode is ServiceMode.Server;
            }, this);

    // Private methods

    private CommandHandler[] ListAllEventHandlers()
    {
        var filter = (Func<CommandHandler, Type, bool>)CommandHandlerResolver.GetType()
            .GetProperty("Filter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(CommandHandlerResolver)!;
        return CommandHandlerRegistry.Handlers
            .Where(h => !h.IsFilter
                && typeof(IEventCommand).IsAssignableFrom(h.CommandType)
                && filter.Invoke(h, h.CommandType))
            .ToArray();
    }
}
