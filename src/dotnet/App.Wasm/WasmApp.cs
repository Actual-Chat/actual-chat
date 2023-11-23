using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Wasm;

public sealed class WasmApp : UI.Blazor.App.AppBase
{
    [Inject] private AppNonScopedServiceStarter AppNonScopedServiceStarter { get; init; } = null!;

    protected override Task OnInitializedAsync()
    {
        _ = AppNonScopedServiceStarter.StartNonScopedServices();
        return base.OnInitializedAsync();
    }
}
