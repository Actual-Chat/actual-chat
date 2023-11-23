using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Net.WebSockets;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App;
#if ANDROID
using Android.Net.Http;
using Xamarin.Android.Net;
#endif

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
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Editor))] // Triggers VTable setup crash
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ILoggerFactory))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Serilog.LoggerConfiguration))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Serilog.ILogger))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(System.Net.WebRequest))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(System.Net.WebResponse))]
#if ANDROID
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidWebChromeClient))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidWebViewClientOverride))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FirebaseMessagingService))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(FirebaseMessagingUtils))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainActivity))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MainApplication))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NativeGoogleAuth))]
    // [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidHttpClient))]
 #pragma warning disable CS0618 // Type or member is obsolete
    // [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AndroidClientHandler))]
 #pragma warning restore CS0618 // Type or member is obsolete
#elif IOS
#endif
    public static void ConfigureServices(IServiceCollection services)
        => AppStartup.ConfigureServices(services, AppKind.MauiApp, c => new HostModule[] {
            new Module.MauiAppModule(c),
        });
}
