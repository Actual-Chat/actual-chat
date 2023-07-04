namespace ActualChat.UI.Blazor.Services;

public static class ServiceProviderExt
{
    public static LocalSettings LocalSettings(this IServiceProvider services)
        => services.GetRequiredService<LocalSettings>();

    public static AccountSettings AccountSettings(this IServiceProvider services)
        => services.GetRequiredService<AccountSettings>();

    public static UIEventHub UIEventHub(this IServiceProvider services)
        => services.GetRequiredService<UIEventHub>();

    public static IJSRuntime JSRuntime(this IServiceProvider services)
        => services.GetRequiredService<IJSRuntime>();

    public static IJSRuntime SafeJSRuntime(this IServiceProvider services)
        => services.GetRequiredService<SafeJSRuntime>();
}
