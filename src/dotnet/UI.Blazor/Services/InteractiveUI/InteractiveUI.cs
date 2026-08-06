using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class InteractiveUI : UIServiceBase<UIHub>, IInteractiveUIBackend
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.InteractiveUI.init";

    private readonly Lock _lock = new();
    private readonly MutableState<bool> _isInteractive;
    private readonly MutableState<ActiveDemandModel?> _activeDemand;

    public new IState<bool> IsInteractive => _isInteractive;
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<ActiveDemandModel?> ActiveDemand => _activeDemand;
    public Task WhenReady { get; }

    public InteractiveUI(UIHub hub) : base(hub)
    {
        var blazorRef = DotNetObjectReference.Create<IInteractiveUIBackend>(this);
        Hub.RegisterDisposable(blazorRef);

        _isInteractive = StateFactory.NewMutable(false);
        _activeDemand = StateFactory.NewMutable((ActiveDemandModel?)null);
        WhenReady = JS.InvokeVoidAsync(JSInitMethod, blazorRef).AsTask();
    }

    [JSInvokable]
    public Task IsInteractiveChanged(bool value)
    {
        lock (_lock)
            _isInteractive.Value = value;
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task Demand(string operation)
    {
        _ = IsInteractiveChanged(false);
        return Demand(operation, CancellationToken.None);
    }

    public async Task<bool> Demand(string operation, CancellationToken cancellationToken)
    {
        if (HostInfo.HostKind == HostKind.MauiApp)
            // MAUI controlled browsers doesn't require user interaction to use sound
            return true;

        await WhenReady.ConfigureAwait(false);
        if (IsInteractive.Value)
            return true;

        // Wait a bit, probably user just pressed "Play" or "Record", but
        // IsInteractive update hasn't made it to Blazor yet.
        var whenInteractive = IsInteractive.Computed.When(x => x, CancellationToken.None);
        await whenInteractive.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).SilentAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsInteractive.Value)
            return true;

        DebugLog?.LogDebug("Demand(), operation = '{Operation}'", operation);

        ActiveDemandModel? activeDemand;
        lock (_lock) { // We need this lock to update ActiveDemand
            activeDemand = _activeDemand.Value;
            if (activeDemand == null) {
                // No active demand, so we need to create modal
                var modalRefTask = ShowModal(whenInteractive);
                activeDemand = new ActiveDemandModel(
                    ImmutableList.Create(operation),
                    modalRefTask,
                    AsyncTaskMethodBuilderExt.New());
                _activeDemand.Value = activeDemand;
            }
            else {
                if (activeDemand.WhenConfirmed.IsCompleted) {
                    // The modal was already closed once, and we don't want to show it multiple times,
                    // so the best we can do is to report that demand is satisfied (or not).
                    return true;
                }
                if (!activeDemand.Operations.Contains(operation))
                    _activeDemand.Value = activeDemand with {
                        Operations = activeDemand.Operations.Add(operation),
                    };
            }
        }

        var modalRef = await activeDemand.WhenModalRef.WaitAsync(cancellationToken).ConfigureAwait(false);
        await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(false);
        return activeDemand.WhenConfirmed.IsCompletedSuccessfully;
    }

    private Task<ModalRef> ShowModal(Task whenInteractive)
    {
        Task<ModalRef> modalRefTask;
        return Dispatcher.InvokeAsync(() => {
            modalRefTask = ModalUI.Show(DemandUserInteractionModal.Model.Instance);
            _ = RemoveActiveDemand();
            _ = CloseWhenInteractive();
            return modalRefTask;
        });

        async Task RemoveActiveDemand() {
            try {
                var modalRef = await modalRefTask.ConfigureAwait(false);
                await modalRef.WhenClosed.ConfigureAwait(false);
            }
            finally {
                lock (_lock) {
                    var activeDemand = _activeDemand.Value;
                    (activeDemand?.WhenConfirmedSource)?.TrySetCanceled();
                    _activeDemand.Value = null;
                }
            }
        }

        async Task CloseWhenInteractive() {
            await whenInteractive.ConfigureAwait(false);
            var modalRef = await modalRefTask.ConfigureAwait(false);
            modalRef.Close();
        }
    }

    // Nested types

    public sealed record ActiveDemandModel(
        ImmutableList<string> Operations,
        Task<ModalRef> WhenModalRef,
        AsyncTaskMethodBuilder WhenConfirmedSource)
    {
        public Task WhenConfirmed => WhenConfirmedSource.Task;
    }
}
