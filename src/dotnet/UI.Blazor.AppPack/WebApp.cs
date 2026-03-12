using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components;

namespace ActualChat.UI.Blazor.App;

// This class lives in AppPack only because we want at least one type to be in this assembly.

#pragma warning disable CA1823 // Avoid unused private fields
public sealed class WebApp : AppBase
{
    // Ensures this assembly references Microsoft.AspNetCore.Components in its metadata,
    // which is required for Razor Source Generator tag helper discovery (SDK 10.0.200+).
    private static readonly Type ComponentBaseType = typeof(ComponentBase);

    private AppNonScopedServiceStarter AppNonScopedServiceStarter
        => field ??= Services.GetRequiredService<AppNonScopedServiceStarter>();

    static WebApp()
        => OtherUIAssemblies = [typeof(WebApp).Assembly, typeof(UIHub).Assembly];

    protected override Task OnInitializedAsync()
    {
        if (OSInfo.IsWebAssembly)
            _ = AppNonScopedServiceStarter.StartNonScopedServices();
        return base.OnInitializedAsync();
    }
}
