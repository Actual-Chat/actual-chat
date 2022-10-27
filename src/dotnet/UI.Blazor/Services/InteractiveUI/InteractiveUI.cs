using ActualChat.Hosting;
using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public class InteractiveUI : IInteractiveUIBackend, IDisposable
{
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private readonly DotNetObjectReference<IInteractiveUIBackend>? _backendRef;

    private ModalUI ModalUI { get; }
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }
    private MomentClockSet Clocks { get; }
    private HostInfo HostInfo { get; }
    private ILogger Log { get; }

    public IMutableState<bool> IsInteractive { get; }
    public Task WhenReady { get; }

    public InteractiveUI(IServiceProvider services)
    {
        Clocks = services.Clocks();
        Log = services.LogFor(GetType());
        HostInfo = services.GetRequiredService<HostInfo>();

        ModalUI = services.GetRequiredService<ModalUI>();
        Dispatcher = services.GetRequiredService<Dispatcher>();
        JS = services.GetRequiredService<IJSRuntime>();
        _backendRef = DotNetObjectReference.Create<IInteractiveUIBackend>(this);

        IsInteractive = services.StateFactory().NewMutable<bool>();
        WhenReady = Dispatcher.InvokeAsync(
            () => JS.InvokeVoidAsync(
                $"{BlazorUICoreModule.ImportName}.InteractiveUI.init",
                _backendRef));
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    [JSInvokable]
    public Task IsInteractiveChanged(bool value)
    {
        IsInteractive.Value = value;
        return Task.CompletedTask;
    }

    public async Task Demand(string operation = "")
    {
        if (HostInfo.AppKind == AppKind.Maui)
            // MAUI controlled browsers doesn't require user interaction to use sound
            return;

        await WhenReady.ConfigureAwait(false);
        if (IsInteractive.Value)
            return;
        // Let's wait a bit - maybe it will become interactive because the interaction just happened
        await IsInteractive
            .When(x => x)
            .WaitAsync(Clocks.CpuClock, TimeSpan.FromSeconds(0.333))
            .SuppressExceptions()
            .ConfigureAwait(false);
        if (IsInteractive.Value)
            return;

        operation = operation.NullIfEmpty() ?? "audio playback or capture";
        Log.LogDebug("Demand(), operation = '{Operation}'", operation);

        using var _1 = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        var modalRef = await Dispatcher.InvokeAsync(() => {
            var model = new DemandUserInteractionModal.Model(operation);
            return ModalUI.Show(model);
        }).ConfigureAwait(false);

        // We're waiting for either becoming interactive or modal close here
        using var cts = new CancellationTokenSource();
        var whenInteractiveTask = IsInteractive.When(x => x, cts.Token);
        await Task.WhenAny(whenInteractiveTask, modalRef.WhenClosed).ConfigureAwait(false);

        // Closing the modal if it isn't closed yet
        if (!modalRef.WhenClosed.IsCompleted)
            await Dispatcher.InvokeAsync(() => {
                modalRef.Close();
            }).SuppressExceptions().ConfigureAwait(false);
    }
}
