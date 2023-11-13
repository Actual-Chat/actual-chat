using System.Diagnostics.CodeAnalysis;

namespace ActualChat.App.Wasm;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class WasmApp : UI.Blazor.App.AppBase
{
    protected override Task OnInitializedAsync()
    {
        _ = AppServiceStarter.StartNonScopedServices();
        return base.OnInitializedAsync();
    }
}
