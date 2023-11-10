using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Session Session(this IServiceProvider services)
        => services.GetRequiredService<Session>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();

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

    public static async ValueTask SafelyDisposeAsync(this IServiceProvider services)
    {
        if (services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (services is IDisposable disposable)
            disposable.Dispose();
    }
}
