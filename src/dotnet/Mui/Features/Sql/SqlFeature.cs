using MudBlazor;

namespace ActualChat.Mui;

public static class SqlFeature
{
    public static IServiceCollection AddMuiSql(this IServiceCollection services)
        => services.AddMuiNavEntry(new("SQL", "/m/system/sql", Icons.Material.Filled.Storage,
            Group: MuiNavGroups.System));
}
