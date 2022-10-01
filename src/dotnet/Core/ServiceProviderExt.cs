namespace ActualChat;

public static class ServiceProviderExt
{
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();
}
