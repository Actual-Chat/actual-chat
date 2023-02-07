using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : UI.Blazor.App.App
{
    [Inject] private IServiceProvider Services { get; init; } = null!;

    protected override void OnInitialized()
    {
        ScopedServicesAccessor.ScopedServices = Services;
        base.OnInitialized();
    }

    public override void Dispose()
    {
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        ScopedServicesAccessor.Forget();
        base.Dispose();
    }

    public MauiBlazorApp()
        => TraceSession.Default.Track("MauiBlazorApp.Constructor");
}
