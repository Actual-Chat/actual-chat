namespace ActualChat.Performance;

public static class ServiceProviderExt
{
    public static Tracer Tracer(this IServiceProvider services)
        => services.GetRequiredService<Tracer>();

    public static Tracer Tracer(this IServiceProvider services, string name)
        => services.Tracer()[name];

    public static Tracer Tracer(this IServiceProvider services, Type type)
        => services.Tracer()[type];

    public static Tracer Tracer<TService>(this IServiceProvider services)
        => services.Tracer()[typeof(TService)];
}
