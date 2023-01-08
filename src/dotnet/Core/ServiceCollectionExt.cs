namespace ActualChat;

public static class ServiceCollectionExt
{
    public static T? GetSingletonInstance<T>(this IServiceCollection services, bool remove = false)
        where T : class
    {
        for (var i = 0; i < services.Count; i++) {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(T) && descriptor.Lifetime == ServiceLifetime.Singleton) {
                if (remove)
                    services.RemoveAt(i);
                return descriptor.ImplementationInstance as T;
            }
        }
        return null;
    }

    public static T AddOrUpdateSingletonInstance<T>(this IServiceCollection services, Func<T> factory, Func<T, T> updater)
        where T : class
    {
        var instance = services.GetSingletonInstance<T>(remove: true);
        instance = instance == null ? factory.Invoke() : updater.Invoke(instance);
        services.AddSingleton(instance);
        return instance;
    }
}
