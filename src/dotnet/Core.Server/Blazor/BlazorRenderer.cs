using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;

namespace ActualChat.Blazor;

public class BlazorRenderer : ProcessorBase
{
    private ServiceProvider Services { get; }
    private HtmlRenderer HtmlRenderer { get; }

    public BlazorRenderer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Services = services.BuildServiceProvider();
        HtmlRenderer = new HtmlRenderer(Services, Services.GetRequiredService<ILoggerFactory>());
    }

    protected override async Task DisposeAsyncCore()
    {
        await HtmlRenderer.DisposeAsync().ConfigureAwait(false);
        await Services.DisposeAsync().ConfigureAwait(false);
    }

    public Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : IComponent
        => RenderComponent<T>(ParameterView.Empty);

    public Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        Dictionary<string, object?> dictionary) where T : IComponent
        => RenderComponent<T>(ParameterView.FromDictionary(dictionary));

    private Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ParameterView parameters) where T : IComponent
        => HtmlRenderer.Dispatcher.InvokeAsync(async () => {
            HtmlRootComponent output = await HtmlRenderer.RenderComponentAsync<T>(parameters).ConfigureAwait(false);
            return output.ToHtmlString();
        });
}
