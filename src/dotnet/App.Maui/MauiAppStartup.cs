using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App;

namespace ActualChat.App.Maui;

public static class MauiAppStartup
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiAppBuilderExt))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiBlazorApp))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiWebView))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiThemeHandler))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NativeHttpClientFactory))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SafeJSRuntime))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SafeJSObjectReference))]
#if ANDROID
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidWebChromeClient))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidWebViewClientOverride))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FirebaseMessagingService))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FirebaseMessagingUtils))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainActivity))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainApplication))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NativeGoogleAuth))]
#elif IOS
#endif
    public static void ConfigureServices(IServiceCollection services)
        => AppStartup.ConfigureServices(services, AppKind.MauiApp, c => new HostModule[] {
            new Module.MauiAppModule(c),
        });
}
