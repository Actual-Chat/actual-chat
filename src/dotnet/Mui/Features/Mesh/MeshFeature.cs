using MudBlazor;

namespace ActualChat.Mui;

public static class MeshFeature
{
    public static IServiceCollection AddMuiMesh(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Mesh", "/m/system/mesh", Icons.Material.Filled.Hub,
            Group: MuiNavGroups.System));
}
