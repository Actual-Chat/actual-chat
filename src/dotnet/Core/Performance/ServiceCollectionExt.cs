namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTracer(this IServiceCollection services)
        => services.AddTransient(c => c.GetService<TracerProvider>()?.Tracer ?? Tracer.Default);
}
