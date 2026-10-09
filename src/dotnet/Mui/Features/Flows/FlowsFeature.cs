using MudBlazor;

namespace ActualChat.Mui;

public static class FlowsFeature
{
    public static IServiceCollection AddMuiFlows(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Flows", "/m/system/flows", Icons.Material.Filled.AccountTree,
            Group: MuiNavGroups.System));
}
