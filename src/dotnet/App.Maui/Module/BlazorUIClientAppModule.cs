using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Bridge;
using Stl.IO;
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
        services.AddSingleton<ReplicaCache>(c => {
            var dbPath = new FilePath(FileSystem.AppDataDirectory) & "ReplicaCache.db3";
            var store = new SQLiteKeyValueStore(dbPath, c).Start();
            var options = new AppReplicaCache.Options(store);
            return new AppReplicaCache(options, c);
        });
    }
}
