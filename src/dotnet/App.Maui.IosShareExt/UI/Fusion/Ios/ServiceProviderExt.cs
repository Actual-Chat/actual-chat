namespace ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IosHub IosHub(this IServiceProvider services)
        => services.GetRequiredService<IosHub>();
}
