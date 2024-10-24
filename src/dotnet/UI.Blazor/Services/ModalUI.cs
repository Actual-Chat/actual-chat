using System.Diagnostics.CodeAnalysis;

namespace ActualChat.UI.Blazor.Services;

public sealed class ModalUI(UIHub uiHub) : IHasServices, IHasAcceptor<ModalHost>
{
    private readonly Acceptor<ModalHost> _hostAcceptor = new(true);

    private TypeMapper<IModalView> ViewResolver { get; } = uiHub.GetRequiredService<TypeMapper<IModalView>>();

    Acceptor<ModalHost> IHasAcceptor<ModalHost>.Acceptor => _hostAcceptor;
    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;

    public UIHub Hub { get; } = uiHub;
    IServiceProvider IHasServices.Services => Hub;
    public Task WhenReady => _hostAcceptor.WhenAccepted();
    public ModalHost Host => _hostAcceptor.Value;

    public Task<ModalRef> Show<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>
        (TModel model, CancellationToken cancellationToken = default)
        where TModel : class
        => Show(model, ModalOptions.Default, cancellationToken);

    public Task<ModalRef> Show<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>
        (TModel model, ModalOptions options, CancellationToken cancellationToken = default)
        where TModel : class
    {
        var componentType = GetComponentType(model);
        return Show(componentType, model, options, cancellationToken).AsTask();
    }

    // Private methods

    private async ValueTask<ModalRef> Show<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType,
        TModel model,
        ModalOptions options,
        CancellationToken cancellationToken)
        where TModel : class
    {
        await WhenReady.ConfigureAwait(true);
        await Host.History.WhenNavigationCompletedOrTimeout().ConfigureAwait(true);
        var content = new RenderFragment(builder => {
            builder.OpenComponent(0, componentType);
            builder.AddAttribute(1, nameof(IModalView<TModel>.ModalModel), model);
            builder.CloseComponent();
        });
        var modalRef = Host.Show(options, model, content);
        // NOTE: Short name goes in beginning to make easier to observe which modal window is used.
        // Long names may be clipped in the Firebase console.
        var modalName = componentType.Name;
        if (!componentType.Namespace.IsNullOrEmpty())
            modalName = modalName + "," + componentType.Namespace;
        AnalyticEvents.RaiseModalStateChanged(modalName, true);
        var registration = cancellationToken.Register(() => modalRef.Close(true), true);
        modalRef.WhenClosed.SilentAwait(false).OnCompleted(() => {
            registration.Dispose();
            AnalyticEvents.RaiseModalStateChanged(modalName, false);
        });
        return modalRef;
    }

#pragma warning disable IL2073
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Type GetComponentType<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>
        (TModel model)
        where TModel : class
        => ViewResolver.Get(model.GetType());
#pragma warning restore IL2073
}
