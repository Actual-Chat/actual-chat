using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Bridge;
using Stl.IO;

namespace ActualChat.App.Maui.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUIClientAppModule : HostModule, IBlazorUIModule
{
    public BlazorUIClientAppModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Auth
        services.AddScoped<IClientAuth, MauiClientAuth>();

        // Replica cache
        services.AddSingleton<AppReplicaCacheConfigurator>();
        services.AddSingleton<ReplicaCache>(c => {
            var dbPath = new FilePath(FileSystem.AppDataDirectory) & "ReplicaCache.db3";
            var store = new SQLiteKeyValueStore(dbPath, c).Start();
            var configurator = c.GetRequiredService<AppReplicaCacheConfigurator>();
            var options = new AppReplicaCache.Options(store) {
                ShouldForceFlushAfterSet = configurator.ShouldForceFlushAfterSet,
            };
            return new AppReplicaCache(options, c);
        });
    }
}
