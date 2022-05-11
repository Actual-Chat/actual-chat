using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class EscapeHandler : IAsyncDisposable
{
    private readonly DotNetObjectReference<EscapeHandler> _blazorRef;
    private readonly IJSRuntime _jsRuntime;

    private IJSObjectReference? _jsRef;

    public event EventHandler? Escape;

    public EscapeHandler(IJSRuntime jsRuntime)
    {
        _blazorRef = DotNetObjectReference.Create(this);
        _jsRuntime = jsRuntime;
    }

    [JSInvokable]
    public void OnEscape()
        => Escape?.Invoke(this, EventArgs.Empty);

    public async Task ConnectAsync(ElementReference elementRef)
        => _jsRef = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                $"{BlazorUICoreModule.ImportName}.EscapeHandler.create",
                elementRef,
                _blazorRef)
            .ConfigureAwait(true);

    public async ValueTask DisposeAsync()
    {
        if (_jsRef == null)
            return;

        await _jsRef.InvokeVoidAsync("dispose").ConfigureAwait(true);
    }
}
