namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTracers(
        this IServiceCollection services, Tracer rootTracer, bool useScopedTracers)
    {
        if (!useScopedTracers || !rootTracer.IsEnabled) {
            services.AddSingleton(rootTracer);
            return services;
        }

        services.AddScoped(_ => new ScopedTracerProvider(rootTracer));
        services.AddTransient(c => c.IsScoped()
            ? c.GetService<ScopedTracerProvider>()?.Tracer ?? rootTracer
            : rootTracer);
        return services;
    }
}
