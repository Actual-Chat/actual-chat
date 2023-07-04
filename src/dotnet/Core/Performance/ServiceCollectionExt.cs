namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTracer(this IServiceCollection services, Tracer? tracer = null)
    {
        if (tracer == null)
            services.AddScoped(_ => new ScopedTracerProvider());
        else
            services.AddScoped(_ => new ScopedTracerProvider(tracer));
        services.AddTransient(c => c.GetService<ScopedTracerProvider>()?.Tracer ?? Tracer.Default);
        return services;
    }
}
