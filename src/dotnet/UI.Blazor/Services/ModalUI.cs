namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages modal dialog display and lifecycle in the UI.
/// </summary>
public sealed class ModalUI : UIServiceBase<UIHub>, IDisposable
{
    private readonly MutableState<IReadOnlyList<ModalRef>> _activeModals;
    private readonly ComputedState<bool> _isAnyFullScreenModalActive;

    private TypeMapper<IModalView> ViewResolver { get; }
    private AnalyticEvents AnalyticEvents => Hub.AnalyticEvents;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;

    public TaskCompletionSource<ModalHost> HostAcceptor { get; } = TaskCompletionSourceExt.New<ModalHost>();
    public Task WhenReady => HostAcceptor.Task;
    public ModalHost Host => field ??= HostAcceptor.Task.RequireResult();
    public IState<IReadOnlyList<ModalRef>> ActiveModals => _activeModals;
    public IState<bool> IsAnyFullScreenModalActive => _isAnyFullScreenModalActive;

    public ModalUI(UIHub hub) : base(hub)
    {
        ViewResolver = hub.Services.GetRequiredService<TypeMapper<IModalView>>();
        var type = GetType();
        _activeModals = StateFactory.NewMutable(
            (IReadOnlyList<ModalRef>)ImmutableList<ModalRef>.Empty,
            StateCategories.Get(type, nameof(_activeModals)));
        _isAnyFullScreenModalActive = StateFactory.NewComputed(
            new ComputedState<bool>.Options {
                UpdateDelayer = FixedDelayer.YieldUnsafe,
                Category = StateCategories.Get(type, nameof(IsAnyFullScreenModalActive)),
            },
            ComputeIsAnyFullScreenModalActive);
    }

    public void Dispose()
        => _isAnyFullScreenModalActive.Dispose();

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

    // Protected/internal methods

    internal void SetActiveModals(IReadOnlyList<ModalRef> activeModals)
    {
        // ModalHost calls this after every render of its own, most of which change nothing - and its
        // list is immutable, so an unchanged set arrives as the very same instance
        if (!ReferenceEquals(_activeModals.Value, activeModals))
            _activeModals.Value = activeModals;
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
        if (!Dispatcher.CheckAccess())
            return await Dispatcher
                .InvokeAsync(() => Show(componentType, model, options, cancellationToken).AsTask())
                .ConfigureAwait(false);

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

    private async Task<bool> ComputeIsAnyFullScreenModalActive(CancellationToken cancellationToken)
    {
        var activeModals = await _activeModals.Use(cancellationToken).ConfigureAwait(false);
        if (activeModals.Count == 0)
            return false;

        var screenSize = await BrowserInfo.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        return activeModals.Any(x => x.IsFullScreen(screenSize.IsNarrow()));
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Type GetComponentType<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TModel>
        (TModel model)
        where TModel : class
        => ViewResolver.Get(model.GetType());
}
