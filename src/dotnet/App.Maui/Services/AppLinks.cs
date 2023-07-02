using ActualChat.UI.Blazor.Services;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui.Services;

public static class AppLinksExt
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

public static class AppLinks
{
    public static void OnAppLinkRequestReceived(Uri uri)
    {
        if (!OrdinalIgnoreCaseEquals(uri.Host, MauiConstants.Host))
            return;

        _ = Task.Run(async () => {
            var scopedServices = await ScopedServicesTask.ConfigureAwait(false);
            var url = new LocalUrl(uri.PathAndQuery + uri.Fragment);
            var autoNavigationUI = scopedServices.GetRequiredService<AutoNavigationUI>();
            _ = autoNavigationUI.DispatchNavigateTo(url, AutoNavigationReason.Notification);
        });
    }
}
