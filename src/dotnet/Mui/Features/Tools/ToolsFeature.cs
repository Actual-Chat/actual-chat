using MudBlazor;

namespace ActualChat.Mui;

public static class ToolsFeature
{
    public static IServiceCollection AddMuiTools(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Tools", "/m/system/tools", Icons.Material.Filled.Build,
            Group: MuiNavGroups.System));
}
