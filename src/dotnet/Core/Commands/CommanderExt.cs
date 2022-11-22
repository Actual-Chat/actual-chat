using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Commands;

public static class CommanderExt
{
    public static CommanderBuilder AddEventHandlers(this CommanderBuilder builder)
    {
        var eventCommandHandlerResolverDescriptor = new ServiceDescriptor(typeof(ICommandHandlerResolver),
            typeof(EventCommandHandlerResolver),
            ServiceLifetime.Singleton);
        if (builder.Services.Any(sd => sd.ServiceType == typeof(ICommandHandlerResolver)))
            builder.Services.Replace(eventCommandHandlerResolverDescriptor);
        else
            builder.Services.TryAdd(eventCommandHandlerResolverDescriptor);
        builder.Services.TryAddSingleton<EventHandlerInvoker>();
        builder.AddHandlers<EventHandlerInvoker>();
        return builder;
    }
}
