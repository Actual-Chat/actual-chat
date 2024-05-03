using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualLab.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.App;

public static class ClientAppStartup
{
    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    public static HostInfo CreateHostInfo(
        IConfiguration cfg,
        string environment,
        string deviceModel,
        HostKind hostKind,
        AppKind appKind,
        string baseUrl,
        bool isTested = false)
        => new() {
            Configuration = cfg,
            Environment = environment.NullIfEmpty() ?? Environments.Development,
            DeviceModel = deviceModel,
            HostKind = hostKind,
            AppKind = appKind,
            Roles = HostRoles.App,
            BaseUrl = baseUrl,
            IsTested = isTested,
        };

    [RequiresUnreferencedCode(UnreferencedCode.Reflection)]
    public static void ConfigureServices(
        IServiceCollection services,
        HostInfo hostInfo,
        Tracer? rootTracer = null)
    {
        // Logging
        services.AddLogging(logging => logging.ConfigureClientFilters(hostInfo.AppKind));

        // Other services shared with plugins
        services.AddSingleton(hostInfo);
        services.AddSingleton(hostInfo.Configuration);
        AppStartup.ConfigureServices(services, hostInfo.HostKind, null, rootTracer);
    }
}
