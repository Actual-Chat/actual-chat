namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTracer(this IServiceCollection services)
    {
        if (services.Any(c => c.ServiceType == typeof(Tracer)))
            return services;
        services.AddScoped(_ => new ScopedTracerProvider());
        services.AddTransient(c => c.GetService<ScopedTracerProvider>()?.Tracer ?? Tracer.Default);
        return services;
    }

    public static IServiceCollection AddTracer(this IServiceCollection services, Tracer tracer)
    {
        services.AddSingleton(tracer);
        return services;
    }
}
