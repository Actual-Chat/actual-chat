using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTracer(this IServiceCollection services)
    {
        services.TryAddScoped<ScopedTracerProvider>(_ => new ScopedTracerProvider());
        services.AddTransient(c => c.GetService<ScopedTracerProvider>()?.Tracer ?? Tracer.Default);
        return services;
    }
}
