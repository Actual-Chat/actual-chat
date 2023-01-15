using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class EscapistSubscription : IAsyncDisposable
{
    private Action? _action;
    private DotNetObjectReference<EscapistSubscription>? _blazorRef;
    private IJSObjectReference? _jsRef;

    public static async ValueTask<IAsyncDisposable> Create(
        IJSRuntime js,
        Action? action,
        CancellationToken cancellationToken)
    {
        var subscription = new EscapistSubscription() {
            _action = action
        };
        subscription._blazorRef = DotNetObjectReference.Create(subscription);
        subscription._jsRef = await js.InvokeAsync<IJSObjectReference>(
            $"{BlazorUICoreModule.ImportName}.EscapistSubscription.create",
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

        _blazorRef?.Dispose();
        try {
            await jsRef.InvokeVoidAsync("dispose", CancellationToken.None).ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        await jsRef.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    [JSInvokable]
    public void OnEscape() => _action?.Invoke();
}
