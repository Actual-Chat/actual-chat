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

    public static IServiceCollection AddTracers(
        this IServiceCollection services, Func<IServiceProvider, Tracer> rootTracerFactory, bool useScopedTracers)
    {
        services.AddSingleton(svp => new RootTracerAccessor(rootTracerFactory(svp)));

        if (!useScopedTracers) {
            services.AddSingleton(svp => svp.GetRequiredService<RootTracerAccessor>().Tracer);
            return services;
        }

        services.AddScoped(svp => {
            var accessor = svp.GetRequiredService<RootTracerAccessor>();
            return new ScopedTracerProvider(accessor.Tracer);
        });
        services.AddTransient(svp => {
            var tracer = svp.IsScoped() ? svp.GetService<ScopedTracerProvider>()?.Tracer : null;
            if (tracer is not null)
                return tracer;

            var accessor = svp.GetRequiredService<RootTracerAccessor>();
            tracer = accessor.Tracer;
            return tracer;
        });
        return services;
    }

    private class RootTracerAccessor(Tracer tracer)
    {
        public Tracer Tracer { get; } = tracer;
    }
}
