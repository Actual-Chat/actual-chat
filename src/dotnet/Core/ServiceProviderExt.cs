using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    public static Session Session(this IServiceProvider services)
        => services.GetRequiredService<Session>();

    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();

    public static IServerKvas ServerKvas(this IServiceProvider services)
        => services.GetRequiredService<IServerKvas>();

    public static async ValueTask SafelyDisposeAsync(this IServiceProvider services)
    {
        if (services is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (services is IDisposable disposable)
            disposable.Dispose();
    }
}
