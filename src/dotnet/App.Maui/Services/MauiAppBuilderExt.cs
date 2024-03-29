using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui.Services;

public static class MauiAppBuilderExt
{
    public static MauiAppBuilder UseAppLinks(this MauiAppBuilder builder)
    {
        builder.ConfigureLifecycleEvents(ConfigureLifecycleEvents);
        return builder;
    }

    private static void ConfigureLifecycleEvents(ILifecycleBuilder events)
    {
        // It seems App.OnAppLinkRequestReceived does not work in MAUI Hybrid Blazor.
        // https://github.com/dotnet/maui/issues/3788#issuecomment-1438888129
        // Workaround is used for Android.
        // TODO: use workaround for iOS as well.
#if ANDROID
        events.AddAndroid(AppLinksWorkaround.ConfigureAndroidLifecycleEvents);
#endif
    }
}
