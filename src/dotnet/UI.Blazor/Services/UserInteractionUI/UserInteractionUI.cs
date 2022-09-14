using ActualChat.UI.Blazor.Module;
using Stl.Locking;

namespace ActualChat.UI.Blazor.Services;

public interface IUserInteractionUIBackend
{
    Task MarkInteractionHappened();
}

public class UserInteractionUI : IUserInteractionUIBackend, IDisposable
{
    private readonly Task _whenInitialized;
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private DotNetObjectReference<IUserInteractionUIBackend>? _blazorRef;

    private ModalUI ModalUI { get; }
    private BlazorCircuitContext BlazorCircuitContext { get; }
    private IJSRuntime JS { get; }
    private MomentClockSet Clocks { get; }

    public IMutableState<bool> IsInteractionHappened { get; }

    public UserInteractionUI(IServiceProvider services)
    {
        ModalUI = services.GetRequiredService<ModalUI>();
        BlazorCircuitContext = services.GetRequiredService<BlazorCircuitContext>();
        JS = services.GetRequiredService<IJSRuntime>();
        Clocks = services.Clocks();

        IsInteractionHappened = services.StateFactory().NewMutable<bool>();
        _whenInitialized = BlazorCircuitContext.Dispatcher.InvokeAsync(() => {
            _blazorRef = DotNetObjectReference.Create<IUserInteractionUIBackend>(this);
            return JS.InvokeVoidAsync(
                $"{BlazorUICoreModule.ImportName}.UserInteractionUI.initialize",
                _blazorRef);
        });
    }

    public void Dispose()
        => _blazorRef?.Dispose();

    [JSInvokable]
    public Task MarkInteractionHappened()
    {
        if (!IsInteractionHappened.Value)
            IsInteractionHappened.Value = true;
        return Task.CompletedTask;
    }

    public async Task RequestInteraction(string operation = "")
    {
        await _whenInitialized.ConfigureAwait(false);
        if (IsInteractionHappened.Value)
            return;
        await Clocks.UIClock.Delay(TimeSpan.FromSeconds(0.5)).ConfigureAwait(false);
        if (IsInteractionHappened.Value)
            return;
        using var _1 = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        if (IsInteractionHappened.Value)
            return;
        await BlazorCircuitContext.Dispatcher.InvokeAsync(async () => {
            var model = new UserInteractionRequestModal.Model(operation.NullIfEmpty() ?? "audio playback or capture");
            var modal = ModalUI.Show(model);
            await modal.WhenClosed.ConfigureAwait(false);
        }).ConfigureAwait(false);
        _ = MarkInteractionHappened();
    }
}
