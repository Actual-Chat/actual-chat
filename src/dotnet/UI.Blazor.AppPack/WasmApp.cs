using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App;

public sealed class WasmApp : AppBase
{
    [Inject] private AppNonScopedServiceStarter AppNonScopedServiceStarter { get; init; } = null!;

    protected override Task OnInitializedAsync()
    {
        _ = AppNonScopedServiceStarter.StartNonScopedServices();
        return base.OnInitializedAsync();
    }
}
