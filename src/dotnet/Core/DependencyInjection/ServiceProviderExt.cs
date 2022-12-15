namespace ActualChat.DependencyInjection;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ServiceFactory<TService, TKey> ServiceFactory<TService, TKey>(this IServiceProvider services)
        => services.GetRequiredService<ServiceFactory<TService, TKey>>();
}
