using Microsoft.Extensions.Configuration;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IConfiguration Configuration(this IServiceProvider services)
        => services.GetRequiredService<IConfiguration>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ModuleHost ModuleHost(this IServiceProvider services)
        => services.GetRequiredService<ModuleHost>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HostInfo HostInfo(this IServiceProvider services)
        => services.GetRequiredService<HostInfo>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Session Session(this IServiceProvider services)
        => services.GetRequiredService<Session>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Features Features(this IServiceProvider services)
        => services.GetRequiredService<Features>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Temporals Temporals(this IServiceProvider services)
        => services.IsScoped()
            ? services.GetService<Temporals>() ?? FakeTemporals.Instance
            : FakeTemporals.Instance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyedFactory<TService, TKey> KeyedFactory<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey>
        (this IServiceProvider services)
        where TService : class
        => services.GetRequiredService<KeyedFactory<TService, TKey>>();
}
