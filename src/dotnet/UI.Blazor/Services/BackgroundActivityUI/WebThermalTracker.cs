
namespace ActualChat.UI.Blazor.Services;

// Must be scoped!
public sealed class WebThermalTracker(IServiceProvider services) : ThermalTracker
{
    public override IState<ThermalLevel> Level
        => field ??= services.GetRequiredService<BrowserInfo>().ThermalLevel;
}
