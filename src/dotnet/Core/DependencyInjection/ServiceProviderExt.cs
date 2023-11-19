using System.Diagnostics.CodeAnalysis;

namespace ActualChat.DependencyInjection;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyedFactory<TService, TKey> KeyedFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey>
        (this IServiceProvider services)
        where TService : class
        => services.GetRequiredService<KeyedFactory<TService, TKey>>();
}
