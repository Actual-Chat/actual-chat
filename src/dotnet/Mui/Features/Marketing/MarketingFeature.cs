using MudBlazor;

namespace ActualChat.Mui;

public static class MarketingFeature
{
    public static IServiceCollection AddMuiMarketing(this IServiceCollection services)
        => services
            .AddMuiNavEntry(new("Campaigns", "/m/marketing/campaigns", Icons.Material.Filled.Campaign,
                Group: MuiNavGroups.Marketing))
            .AddMuiNavEntry(new("Keywords", "/m/marketing/keywords", Icons.Material.Filled.Search,
                Group: MuiNavGroups.Marketing))
            .AddMuiNavEntry(new("Milestones", "/m/marketing/milestones", Icons.Material.Filled.Flag,
                Group: MuiNavGroups.Marketing));
}
