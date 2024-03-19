namespace ActualChat.Queues.Internal;

public static class CommandHandlerExt
{
    private static readonly ConcurrentDictionary<Type, Func<CommandHandler, Type>?> ServiceTypeGetterCache = new();

    public static Type? GetServiceType(this CommandHandler commandHandler)
    {
        var serviceTypeGetter = ServiceTypeGetterCache.GetOrAdd(commandHandler.GetType(), type => {
            if (!type.IsGenericType)
                return null;

            var gtd = type.GetGenericTypeDefinition();
            if (gtd == typeof(MethodCommandHandler<>) || gtd == typeof(InterfaceCommandHandler<>))
                return type.GetProperty("ServiceType")!.GetGetter<CommandHandler, Type>();

            return null;
        });

        return serviceTypeGetter?.Invoke(commandHandler);
    }
}
