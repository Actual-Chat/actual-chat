using ActualChat.UI.Blazor.App;
using Microsoft.Maui.LifecycleEvents;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddTransient<IDeviceTokenRetriever>(_ => new MacDeviceTokenRetriever());
        services.AddScoped<INotificationsPermission>(_ => new MacNotificationsPermission());
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    { }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
    { }
}
