using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App;

public sealed class WebApp : AppBase
{
    private AppNonScopedServiceStarter AppNonScopedServiceStarter
        => field ??= Services.GetRequiredService<AppNonScopedServiceStarter>();

    protected override Task OnInitializedAsync()
    {
        if (OSInfo.IsWebAssembly)
            _ = AppNonScopedServiceStarter.StartNonScopedServices();

        return base.OnInitializedAsync();
    }
}
