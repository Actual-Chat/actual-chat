namespace ActualChat;

public static class ServiceCollectionExt
{
    public static IServiceCollection ReplaceFactory<T>(
        this IServiceCollection services,
        Func<IServiceProvider, Func<T>, T> newFactory)
        where T : class
    {
        // Last registration wins, so we need to go backwards
        var isRegistered = false;
        for (var i = services.Count - 1; i >= 0; i--) {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(T)) {
                isRegistered = true;
                if (descriptor.ImplementationFactory is { } implementationFactory) {
                    services[i] = new ServiceDescriptor(
                        descriptor.ServiceType,
                        c => newFactory.Invoke(c, () => (T)implementationFactory.Invoke(c)),
                        descriptor.Lifetime);
                    return services;
                }
            }
        }
        throw isRegistered
            ? new KeyNotFoundException($"'{typeof(T).GetName()}' is registered as an instance, not as a factory.")
            : new KeyNotFoundException($"'{typeof(T).GetName()}' is not registered.");
    }
}
