using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class EscapistSubscription : IAsyncDisposable
{
    private static readonly string JSCreateMethod = $"{BlazorUICoreModule.ImportName}.EscapistSubscription.create";

    private Action? _action;
    private bool _once;
    private DotNetObjectReference<EscapistSubscription>? _blazorRef;
    private IJSObjectReference? _jsRef;

    public static async ValueTask<IAsyncDisposable> Create(
        IJSRuntime js,
        Action action,
        bool once,
        CancellationToken cancellationToken)
    {
        var subscription = new EscapistSubscription {
            _action = action,
            _once = once,
        };
        subscription._blazorRef = DotNetObjectReference.Create(subscription);
        subscription._jsRef = await js.InvokeAsync<IJSObjectReference>(
            JSCreateMethod,
            cancellationToken,
            subscription._blazorRef
            ).ConfigureAwait(false);
        return subscription;
    }

    public async ValueTask DisposeAsync()
    {
        var jsRef = Interlocked.Exchange(ref _jsRef, null);
        if (jsRef == null)
            return;

        await jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _blazorRef.DisposeSilently();
        _blazorRef = null;
    }

    [JSInvokable]
    public void OnEscape() {
        _action?.Invoke();

        if (_once)
            _ = DisposeAsync().ConfigureAwait(false);
    }
}
