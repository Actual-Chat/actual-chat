using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App;

// This class lives in AppPack only because we want at least one type to be in this assembly.

public sealed class WebApp : AppBase
{
    [field: AllowNull, MaybeNull]
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
