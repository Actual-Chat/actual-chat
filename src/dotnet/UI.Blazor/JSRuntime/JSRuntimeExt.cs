using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

public static class JSRuntimeExt
{
    // Disconnection handling

    public static bool IsDisconnected(this IJSRuntime js)
        => SafeJSRuntime.IsDisconnected(js);

    public static IJSRuntime? IfConnected(this IJSRuntime js)
        => js.IsDisconnected() ? null : js;

    public static IJSRuntime RequireConnected(this IJSRuntime js)
        => IsDisconnected(js) ? throw JSRuntimeErrors.Disconnected() : js;

    // EvalXxx

    public static ValueTask EvalVoid(this IJSRuntime js, string expression, CancellationToken cancellationToken = default)
        => js.InvokeVoidAsync("eval", cancellationToken, expression);

    public static ValueTask<T> Eval<T>(this IJSRuntime js, string expression, CancellationToken cancellationToken = default)
        => js.InvokeAsync<T>("eval", cancellationToken, expression);
}
