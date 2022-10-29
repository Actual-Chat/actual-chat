using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class EscapistSubscription
{
    private readonly IJSRuntime _jsRuntime;
    private Action? _escapeAction;

    public EscapistSubscription(IJSRuntime jsRuntime) => _jsRuntime = jsRuntime;

    [JSInvokable]
    public void OnEscape() => _escapeAction?.Invoke();

    public async Task<IAsyncDisposable> SubscribeAsync(Action action, CancellationToken token)
    {
        _escapeAction = action ?? throw new ArgumentNullException(nameof(action));

        var factory = $"{BlazorUICoreModule.ImportName}.EscapistSubscription.create";
        var blazorRef = DotNetObjectReference.Create(this);
        var jsRef = await _jsRuntime.InvokeAsync<IJSObjectReference>(factory, token, blazorRef).ConfigureAwait(false);
        return AsyncDisposable.New(async () => {
            await jsRef.InvokeVoidAsync("dispose", CancellationToken.None).ConfigureAwait(false);
            await jsRef.DisposeAsync().AsTask().ConfigureAwait(false);
        });
    }
}
