namespace ActualChat.App.Maui.Services.StartupTracing;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddDispatcherProxy(this IServiceCollection services, bool logAllOperations)
    {
        var dispatcherDescriptor = services.FirstOrDefault(c => c.ServiceType == typeof(IDispatcher));
        if (dispatcherDescriptor?.ImplementationFactory == null)
            return services;

        object ImplementationFactory(IServiceProvider svp)
            => new DispatcherProxy(
                (IDispatcher)dispatcherDescriptor.ImplementationFactory(svp),
                logAllOperations);

        services.Remove(dispatcherDescriptor);
        services.Add(new ServiceDescriptor(
            dispatcherDescriptor.ServiceType,
            ImplementationFactory,
            dispatcherDescriptor.Lifetime));
        return services;
    }
}
