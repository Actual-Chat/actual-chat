using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : UI.Blazor.App.AppBase
{
    [Inject] private IServiceProvider Services { get; init; } = null!;

    protected override void OnInitialized()
    {
        ScopedServices = Services;
        base.OnInitialized();
    }

    public override void Dispose()
    {
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        DiscardScopedServices();
        base.Dispose();
    }

    public MauiBlazorApp()
        => Tracer.Default.Point("MauiBlazorApp.ctor");
}
