using MudBlazor;

namespace ActualChat.Mui;

public static class OperationsFeature
{
    public static IServiceCollection AddMuiOperations(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Operations", "/m/system/operations", Icons.Material.Filled.Sync,
            Group: MuiNavGroups.System));
}
