using ActualChat.App.Maui.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.App.Maui;

public class MauiBlazorApp : ComponentBase, IDisposable
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(MauiBlazorApp)];

    [Inject] private IServiceProvider Services { get; init; } = null!;

    protected override void OnInitialized()
    {
        _tracer.Point(nameof(OnInitialized));
        ScopedServices = Services;
    }

    public void Dispose()
        // On refreshing page, MAUI dispose PageContext.
        // Which dispose Renderer with all components.
        // And after that container is disposed.
        // So we forget previous scoped services container in advance.
        => DiscardScopedServices();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<UI.Blazor.App.AppBase>(0);
        var clientAppSettings = Services.GetRequiredService<ClientAppSettings>();
        builder.AddAttribute(1, nameof(UI.Blazor.App.AppBase.SessionId), clientAppSettings.Session.Id.Value);
        builder.CloseComponent();
    }
}
