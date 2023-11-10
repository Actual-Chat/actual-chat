namespace ActualChat.UI.Blazor.Services;

public static class ServiceProviderExt
{
    public static UIEventHub UIEventHub(this IServiceProvider services)
        => services.GetRequiredService<UIEventHub>();

    public static IJSRuntime JSRuntime(this IServiceProvider services)
        => services.GetRequiredService<IJSRuntime>();
}
