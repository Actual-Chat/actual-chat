namespace ActualChat;

public static class ServiceProviderExt
{
    public static UriMapper UriMapper(this IServiceProvider services)
        => services.GetRequiredService<UriMapper>();

    // Logging extensions

    public static ILoggerFactory Logs(this IServiceProvider services)
        => services.GetRequiredService<ILoggerFactory>();
    public static ILogger LogFor<T>(this IServiceProvider services)
        => services.LogFor(typeof(T));
    public static ILogger LogFor(this IServiceProvider services, Type type)
        => services.Logs().CreateLogger(type);
    public static ILogger LogFor(this IServiceProvider services, string category)
        => services.Logs().CreateLogger(category);
}
