namespace ActualChat;

public static class ServiceProviderExt
{
    public static UriMapper UriMapper(this IServiceProvider services)
        => services.GetRequiredService<UriMapper>();
}
