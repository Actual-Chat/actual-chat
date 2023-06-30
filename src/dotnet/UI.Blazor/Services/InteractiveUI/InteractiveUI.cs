using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class InteractiveUI : IInteractiveUIBackend, IDisposable
{
    private readonly DotNetObjectReference<IInteractiveUIBackend>? _backendRef;
    private readonly IMutableState<bool> _isInteractive;
    private readonly IMutableState<ActiveDemandModel?> _activeDemand;
    private Dispatcher? _dispatcher;
    private readonly object _lock = new();

    // Services
    private IServiceProvider Services { get; }
    private ModalUI ModalUI { get; }
    private Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    private IJSRuntime JS { get; }
    private HostInfo HostInfo { get; }
    private ILogger Log { get; }

    public IState<bool> IsInteractive => _isInteractive;
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<ActiveDemandModel?> ActiveDemand => _activeDemand;
    public Task WhenReady { get; }

    public InteractiveUI(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HostInfo = services.GetRequiredService<HostInfo>();

        ModalUI = services.GetRequiredService<ModalUI>();
        JS = services.GetRequiredService<IJSRuntime>();
        _backendRef = DotNetObjectReference.Create<IInteractiveUIBackend>(this);

        _isInteractive = services.StateFactory().NewMutable(false);
        _activeDemand = services.StateFactory().NewMutable((ActiveDemandModel?)null);
        WhenReady = JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.InteractiveUI.init",
            _backendRef
            ).AsTask();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    [JSInvokable]
    public Task IsInteractiveChanged(bool value)
    {
        lock (_lock) {
            _isInteractive.Value = value;
            _activeDemand.Value = null;
        }
        return Task.CompletedTask;
    }

    public async Task<bool> Demand(string operation, CancellationToken cancellationToken)
    {
        if (HostInfo.AppKind == AppKind.MauiApp)
            // MAUI controlled browsers doesn't require user interaction to use sound
            return true;

        await WhenReady.ConfigureAwait(false);
        if (IsInteractive.Value)
            return true;

        // Wait a bit, probably user just pressed "Play" or "Record", but
        // IsInteractive update hasn't made it to Blazor yet.
        await IsInteractive
            .When(x => x, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
            .SuppressExceptions(e => e is TimeoutException)
            .ConfigureAwait(false);
        if (IsInteractive.Value)
            return true;

        Log.LogDebug("Demand(), operation = '{Operation}'", operation);

        ActiveDemandModel? activeDemand;
        lock (_lock) { // We need this lock to update ActiveDemand
            activeDemand = _activeDemand.Value;
            if (activeDemand == null) {
                // No active demand, so we need to create modal
                var modalRefTask = ShowModal();
                activeDemand = new ActiveDemandModel(
                    ImmutableList.Create(operation),
                    modalRefTask,
                    TaskCompletionSourceExt.New());
                _activeDemand.Value = activeDemand;
            }
            else {
                if (activeDemand.WhenConfirmed.IsCompleted) {
                    // The modal was already closed once, and we don't want to show it multiple times,
                    // so the best we can do is to report that demand is satisfied (or not).
                    return true;
                }
                if (!activeDemand.Operations.Contains(operation, StringComparer.Ordinal))
                    _activeDemand.Value = activeDemand with {
                        Operations = activeDemand.Operations.Add(operation),
                    };
            }
        }

        var modalRef = await activeDemand.WhenModalRef.WaitAsync(cancellationToken).ConfigureAwait(false);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);

        var isConfirmed = activeDemand.WhenConfirmed.IsCompletedSuccessfully;
        if (isConfirmed) // If confirmed, let's wait for interactivity as well
            await IsInteractive.When(x => x, cancellationToken).ConfigureAwait(false);

        return isConfirmed;
    }

    private Task<ModalRef> ShowModal()
    {
        var modalRefTask = Dispatcher.InvokeAsync(() => ModalUI.Show(DemandUserInteractionModal.Model.Instance));
        _ = modalRefTask.ContinueWith(async _ => {
            if (modalRefTask.IsCompletedSuccessfully) {
                // If modal was successfully created, let's wait when it gets closed
                var modalRef = await modalRefTask.ConfigureAwait(false);
                await modalRef.WhenClosed.ConfigureAwait(false);
            }
            lock (_lock) {
                var activeDemand = _activeDemand.Value;
                var whenConfirmed = activeDemand?.WhenConfirmedSource;
                whenConfirmed?.TrySetCanceled();
            }
        }, TaskScheduler.Default);
        return modalRefTask;
    }

    // Nested types

    public sealed record ActiveDemandModel(
        ImmutableList<string> Operations,
        Task<ModalRef> WhenModalRef,
        TaskCompletionSource WhenConfirmedSource)
    {
        public Task WhenConfirmed => WhenConfirmedSource.Task;
    }
}
