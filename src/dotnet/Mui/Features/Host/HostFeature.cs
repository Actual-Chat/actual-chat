using MudBlazor;

namespace ActualChat.Mui;

public static class HostFeature
{
    public static IServiceCollection AddMuiHost(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Host", "/m/system/host", Icons.Material.Filled.Dns,
            Group: MuiNavGroups.System));
}
