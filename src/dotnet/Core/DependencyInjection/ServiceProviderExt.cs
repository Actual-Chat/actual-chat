namespace ActualChat.DependencyInjection;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyedFactory<TService, TKey> KeyedFactory<TService, TKey>(this IServiceProvider services)
        where TService : class
        => services.GetRequiredService<KeyedFactory<TService, TKey>>();
}
