using MudBlazor;

namespace ActualChat.Mui;

public static class DigestFeature
{
    public static IServiceCollection AddMuiDigest(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Digest", "/m/system/digest", Icons.Material.Filled.Email,
            Group: MuiNavGroups.System));
}
