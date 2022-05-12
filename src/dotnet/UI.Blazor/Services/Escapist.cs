using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class Escapist : IAsyncDisposable
{
    private readonly DotNetObjectReference<Escapist> _blazorRef;
    private readonly IJSRuntime _jsRuntime;

    private IJSObjectReference? _jsRef;

    public event Action? Escape;

    public Escapist(IJSRuntime jsRuntime)
    {
        _blazorRef = DotNetObjectReference.Create(this);
        _jsRuntime = jsRuntime;
    }

    [JSInvokable]
    public void OnEscape() => Escape?.Invoke();

    public async Task ConnectAsync()
    {
        var factory = $"{BlazorUICoreModule.ImportName}.EscapeHandler.create";
        _jsRef = await _jsRuntime.InvokeAsync<IJSObjectReference>(factory, _blazorRef).ConfigureAwait(true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsRef == null)
            return;

        await _jsRef.InvokeVoidAsync("dispose").ConfigureAwait(true);
        _jsRef = null;
    }
}
