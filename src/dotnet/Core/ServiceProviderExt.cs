using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HostInfo HostInfo(this IServiceProvider services)
        => services.GetRequiredService<HostInfo>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Session Session(this IServiceProvider services)
        => services.GetRequiredService<Session>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Features Features(this IServiceProvider services)
        => services.GetRequiredService<Features>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IServerKvas ServerKvas(this IServiceProvider services)
        => services.GetRequiredService<IServerKvas>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalSettings LocalSettings(this IServiceProvider services)
        => services.GetRequiredService<LocalSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AccountSettings AccountSettings(this IServiceProvider services, Session session)
        => new(services.ServerKvas(), session);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AccountSettings AccountSettings(this IServiceProvider services)
        => services.GetRequiredService<AccountSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static KeyedFactory<TService, TKey> KeyedFactory<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TKey>
        (this IServiceProvider services)
        where TService : class
        => services.GetRequiredService<KeyedFactory<TService, TKey>>();
}
