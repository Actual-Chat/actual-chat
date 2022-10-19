using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();

    public static IServerKvas ServerKvas(this IServiceProvider services)
        => services.GetRequiredService<IServerKvas>();
}
