namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UrlMapper UrlMapper(this IServiceProvider services)
        => services.GetRequiredService<UrlMapper>();
}
