using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui;

public static class MauiAppStartup
{
    // The rest is in ILLink.Descriptors.Maui.xml
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DotNetObjectReference<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EventCallback<>))]
    public static void ConfigureServices(IServiceCollection services)
        => AppStartup.ConfigureServices(services, AppKind.MauiApp, c => new HostModule[] {
            new Module.MauiAppModule(c),
        });
}
