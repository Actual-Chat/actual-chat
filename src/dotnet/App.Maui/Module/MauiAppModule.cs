using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MauiAppModule : HostModule, IBlazorUIModule
{
    public MauiAppModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Session
        services.AddScoped<ISessionProvider>(c => new MauiSessionProvider(c));

        // Auth
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));
        services.AddSingleton(c => new BaseUrlProvider(c.GetRequiredService<UrlMapper>().BaseUrl));
        services.AddTransient(c => new MobileAuthClient(
            c.GetRequiredService<HttpClient>(),
            c.GetRequiredService<ILogger<MobileAuthClient>>()));

        // UI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));

        // Misc.
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));

        // Replica cache
        // Temporarily disabled for MAUI due to issues there
#if false
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
#endif
    }
}
