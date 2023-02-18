using System.Diagnostics.CodeAnalysis;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI : IHasServices, IHasAcceptor<ModalHost>
{
    private readonly Acceptor<ModalHost> _hostAcceptor = new(true);

    private BrowserInfo BrowserInfo { get; }
    private History History { get; }
    private TuneUI TuneUI { get; }
    private IMatchingTypeFinder MatchingTypeFinder { get; }

    Acceptor<ModalHost> IHasAcceptor<ModalHost>.Acceptor => _hostAcceptor;

    public IServiceProvider Services { get; }
    public Task WhenReady => _hostAcceptor.WhenAccepted();
    public ModalHost Host => _hostAcceptor.Value;

    public ModalUI(IServiceProvider services)
    {
        Services = services;
        BrowserInfo = services.GetRequiredService<BrowserInfo>();
        History = services.GetRequiredService<History>();
        TuneUI = services.GetRequiredService<TuneUI>();
        MatchingTypeFinder = services.GetRequiredService<IMatchingTypeFinder>();
    }

    public async Task<ModalRef> Show<TModel>(TModel model, bool isFullScreen = false)
        where TModel : class
    {
        var options = new ModalOptions() {
            OverlayClass = isFullScreen ? "modal-overlay-fullscreen" : "",
        };
        return await Show(model, options);
    }

    public async Task<ModalRef> Show<TModel>(TModel model, ModalOptions options)
        where TModel : class
    {
        var componentType = GetComponentType(model);
        return await Show(componentType, model, options);
    }

    // Private methods

    private async ValueTask<ModalRef> Show<TModel>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType,
        TModel model,
        ModalOptions options)
        where TModel : class
    {
        await WhenReady;
        var content = new RenderFragment(builder => {
            builder.OpenComponent(0, componentType);
            builder.AddAttribute(1, nameof(IModalView<TModel>.ModalModel), model);
            builder.CloseComponent();
        });
        return Host.Show(options, model, content);
    }

#pragma warning disable IL2073
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Type GetComponentType<TModel>(TModel model)
        where TModel : class
    {
        var componentType = MatchingTypeFinder.TryFind(model.GetType(), typeof(IModalView));
        return componentType
            ?? throw StandardError.NotFound<IModalView>(
                $"No modal view component for '{model.GetType().GetName()}' model.");
    }
#pragma warning restore IL2073
}
