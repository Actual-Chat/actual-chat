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

        var localUrl = uri.PathAndQuery + uri.Fragment;
        _ = Handle();

        async Task Handle()
        {
            await WhenScopedServicesReady.ConfigureAwait(true);
            var log = ScopedServices.LogFor(typeof(AppLinks));
            log.LogDebug("AppLink navigates to '{Url}'", localUrl);
            var navigationCoordinatorUI = ScopedServices.GetRequiredService<NavigationCoordinatorUI>();
            var dispatcher = ScopedServices.GetRequiredService<Microsoft.AspNetCore.Components.Dispatcher>();
            _ = dispatcher.CheckAccess()
                ? navigationCoordinatorUI.HandleNavigationRequest(localUrl)
                : dispatcher.InvokeAsync(async ()
                    => await navigationCoordinatorUI.HandleNavigationRequest(localUrl).ConfigureAwait(false));
        }
    }
}
