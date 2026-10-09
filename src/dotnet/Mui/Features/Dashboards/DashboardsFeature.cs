using MudBlazor;

namespace ActualChat.Mui;

public static class DashboardsFeature
{
    public static IServiceCollection AddMuiDashboards(this IServiceCollection services)
        => services
            .AddMuiNavEntry(new("Overview", NavMenu.Home, Icons.Material.Filled.Dashboard, 0,
                MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Chats", "/m/dashboards/chats", Icons.Material.Filled.Forum,
                Group: MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Messages", "/m/dashboards/messages", Icons.Material.Filled.Message,
                Group: MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Notifications", "/m/dashboards/notifications", Icons.Material.Filled.Notifications,
                Group: MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Sessions", "/m/dashboards/sessions", Icons.Material.Filled.Devices,
                Group: MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Uploads", "/m/dashboards/uploads", Icons.Material.Filled.CloudUpload,
                Group: MuiNavGroups.Dashboards))
            .AddMuiNavEntry(new("Users", "/m/dashboards/users", Icons.Material.Filled.People,
                Group: MuiNavGroups.Dashboards));
}
