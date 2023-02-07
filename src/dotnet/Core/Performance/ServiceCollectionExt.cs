namespace ActualChat.Performance;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddTraceSession(
        this IServiceCollection services,
        ITraceSession trace)
    {
        services.AddSingleton(trace);
        return services;
    }

    public static IServiceCollection AddTraceSession(
        this IServiceCollection services,
        Func<IServiceProvider, ITraceAccessor> implementationFactory)
    {
        services.AddTransient(implementationFactory);
        services.AddTransient<ITraceSession>(c => c.GetRequiredService<ITraceAccessor>().Trace ?? TraceSession.Null);
        return services;
    }
}
