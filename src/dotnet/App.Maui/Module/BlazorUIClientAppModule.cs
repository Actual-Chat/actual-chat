using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;
using Stl.Plugins;

namespace ActualChat.App.Maui.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class BlazorUIClientAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIClientAppModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUIClientAppModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        // Auth
        services.AddScoped<IClientAuth, MauiClientAuth>();

        // Replica cache
        services.AddSingleton<IndexedDbReplicaCacheStorage.JSRuntimeAccessor>(_ =>
            new IndexedDbReplicaCacheStorage.JSRuntimeAccessor(
                () => ScopedServicesAccessor.ScopedServices.GetRequiredService<IJSRuntime>()));
        services.AddSingleton<IReplicaCacheStorage>(c => new SQLiteReplicaCacheStore(
            c.LogFor<SQLiteReplicaCacheStore>()));
    }
}
