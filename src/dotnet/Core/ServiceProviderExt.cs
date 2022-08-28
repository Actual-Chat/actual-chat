namespace ActualChat;

public static class ServiceProviderExt
{
    public static IHttpClientFactory HttpClientFactory(this IServiceProvider services)
        => services.GetRequiredService<IHttpClientFactory>();

    public static UriMapper UriMapper(this IServiceProvider services)
        => services.GetRequiredService<UriMapper>();
}
