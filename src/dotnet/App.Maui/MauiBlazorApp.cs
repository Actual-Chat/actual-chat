using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : ComponentBase, IDisposable
{
    [Inject] private IServiceProvider Services { get; init; } = null!;

    protected override void OnInitialized()
    {
        Tracer.Default.Point("MauiBlazorApp.OnInitialized");
        ScopedServices = Services;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<UI.Blazor.App.AppBase>(0);
        var clientAppSettings = Services.GetRequiredService<ClientAppSettings>();
        builder.AddAttribute(1, nameof(UI.Blazor.App.AppBase.SessionId), clientAppSettings.SessionId);
        builder.CloseComponent();
    }

    public void Dispose()
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        => DiscardScopedServices();

    public MauiBlazorApp()
        => Tracer.Default.Point("MauiBlazorApp.ctor");
}
