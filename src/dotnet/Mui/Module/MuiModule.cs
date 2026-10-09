using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;

namespace ActualChat.Mui.Module;

public sealed class MuiModule(IServiceProvider moduleServices)
    : HostModule<MuiSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // The backend part is registered everywhere: backend hosts run it, other hosts get a routing client
        var rpcHost = services.AddRpcHost(HostInfo);
        rpcHost.AddBackend<IMuiDbBackend, MuiDbBackend>();

        // Everything else lives on API hosts only
        if (!HostInfo.HasRole(HostRole.Api))
            return;

        services.AddMudServices();
        services.AddSingleton<IMuiDb>(c => new MuiDb(c));
        services.AddScoped(c => new MuiActions(c));
        services.AddScoped(c => new MuiTime(c.GetRequiredService<IJSRuntime>()));
        services.AddScoped(c => new MuiPeriodState(c.GetRequiredService<ProtectedLocalStorage>()));

        services.AddMuiNavGroup(new(MuiNavGroups.Dashboards, Icons.Material.Filled.Assessment, 10));
        services.AddMuiNavGroup(new(MuiNavGroups.System, Icons.Material.Filled.Settings, 20));
        services.AddMuiDashboards();
        services.AddMuiDigest();
        services.AddMuiEvents();
        services.AddMuiFlows();
        services.AddMuiHost();
        services.AddMuiMesh();
        services.AddMuiOperations();
        services.AddMuiSql();
        services.AddMuiTools();
    }
}
