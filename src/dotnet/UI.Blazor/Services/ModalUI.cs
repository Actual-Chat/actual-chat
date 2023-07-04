using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI : IHasServices, IHasAcceptor<ModalHost>
{
    private readonly Acceptor<ModalHost> _hostAcceptor = new(true);

    private BrowserInfo BrowserInfo { get; }
    private History History { get; }
    private TuneUI TuneUI { get; }
    private TypeMapper<IModalView> ViewResolver { get; }

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
        ViewResolver = services.GetRequiredService<TypeMapper<IModalView>>();
    }

    public Task<ModalRef> Show<TModel>(TModel model, bool isFullScreen = false)
        where TModel : class
    {
        var options = isFullScreen ? ModalOptions.FullScreen : ModalOptions.Default;
        return Show(model, options);
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
        => ViewResolver.Get(model.GetType());
#pragma warning restore IL2073
}
