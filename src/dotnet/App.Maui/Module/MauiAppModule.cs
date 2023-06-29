using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Client.Caching;
using Stl.IO;

namespace ActualChat.App.Maui.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MauiAppModule : HostModule, IBlazorUIModule
{
    public MauiAppModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Session
        services.AddScoped<ISessionResolver>(c => new MauiSessionProvider(c));

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

        // ClientComputedCache
        services.AddSingleton(_ => new SQLiteClientComputedCache.Options() {
            DbPath = new FilePath(FileSystem.AppDataDirectory) & "CCC.db3",
        });
        services.AddSingleton(c => new SQLiteClientComputedCache(
            c.GetRequiredService<SQLiteClientComputedCache.Options>(), c));
        services.AddAlias<IClientComputedCache, SQLiteClientComputedCache>();
    }
}
