namespace ActualChat.Performance;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Tracer Tracer(this IServiceProvider services)
        => services.GetRequiredService<Tracer>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Tracer Tracer(this IServiceProvider services, string name)
        => services.Tracer()[name];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Tracer Tracer(this IServiceProvider services, Type type)
        => services.Tracer()[type];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Tracer Tracer<TService>(this IServiceProvider services)
        => services.Tracer()[typeof(TService)];
}
