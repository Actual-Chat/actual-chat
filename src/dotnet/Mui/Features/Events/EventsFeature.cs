using MudBlazor;

namespace ActualChat.Mui;

public static class EventsFeature
{
    public static IServiceCollection AddMuiEvents(this IServiceCollection services)
        => services.AddMuiNavEntry(new("Events", "/m/system/events", Icons.Material.Filled.Bolt,
            Group: MuiNavGroups.System));
}
