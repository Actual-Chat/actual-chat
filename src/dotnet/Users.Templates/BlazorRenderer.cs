using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ActualChat.Users.Templates;

public class BlazorRenderer : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HtmlRenderer _htmlRenderer;

    public BlazorRenderer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
        _loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        _htmlRenderer = new HtmlRenderer(_serviceProvider, _loggerFactory);
    }

    public async ValueTask DisposeAsync()
    {
        await _htmlRenderer.DisposeAsync().ConfigureAwait(false);
        _loggerFactory.Dispose();
        await _serviceProvider.DisposeAsync().ConfigureAwait(false);
    }

    public Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>()
        where T : IComponent
        => RenderComponent<T>(ParameterView.Empty);

    public Task<string> RenderComponent(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type componentType)
        => RenderComponent(componentType, ParameterView.Empty);

    public Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        Dictionary<string, object?> dictionary) where T : IComponent
        => RenderComponent<T>(ParameterView.FromDictionary(dictionary));

    private Task<string> RenderComponent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        ParameterView parameters) where T : IComponent
        => _htmlRenderer.Dispatcher.InvokeAsync(async () => {
            var output = await _htmlRenderer.RenderComponentAsync<T>(parameters).ConfigureAwait(false);
            return output.ToHtmlString();
        });

    private Task<string> RenderComponent(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type componentType,
        ParameterView parameters)
        => _htmlRenderer.Dispatcher.InvokeAsync(async () => {
            var output = await _htmlRenderer.RenderComponentAsync(componentType, parameters).ConfigureAwait(false);
            return output.ToHtmlString();
        });
}
