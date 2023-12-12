namespace ActualChat.UI.Blazor;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UIHub UIHub(this IServiceProvider services)
        => services.GetRequiredService<UIHub>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UIEventHub UIEventHub(this IServiceProvider services)
        => services.GetRequiredService<UIEventHub>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IJSRuntime JSRuntime(this IServiceProvider services)
        => services.GetRequiredService<IJSRuntime>();
}
