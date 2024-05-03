using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App;
using ActualLab.Internal;
using Microsoft.AspNetCore.Components.WebView;

namespace ActualChat.App.Maui;

public static class MauiAppStartup
{
    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(WebViewManager))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.Maui.AndroidWebKitWebViewManager", "Microsoft.AspNetCore.Components.WebView.Maui")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon.IncomingMessageType", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcCommon.OutgoingMessageType", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcSender", "Microsoft.AspNetCore.Components.WebView")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All,
        "Microsoft.AspNetCore.Components.WebView.IpcReceiver", "Microsoft.AspNetCore.Components.WebView")]
    public static void ConfigureServices(IServiceCollection services)
        => AppStartup.ConfigureServices(services, HostKind.MauiApp, c => [new Module.MauiAppModule(c)]);
}
